using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace MMW.RuleEngine.Tests.Constitution;

/// <summary>Một lời gọi tìm thấy trong thân phương thức.</summary>
public sealed record CallSite(string CallerType, string CallerMethod, string TargetType, string TargetMember)
{
    public override string ToString() => $"{CallerType}.{CallerMethod} → {TargetType}::{TargetMember}";
}

/// <summary>
/// Quét mã IL của một assembly để liệt kê mọi lời gọi phương thức.
/// </summary>
/// <remarks>
/// Phải đọc IL chứ không thể dùng reflection thường: reflection cho biết một lớp có những
/// thành viên gì, chứ không cho biết THÂN phương thức gọi những gì. Mà thứ cần gác lại nằm
/// đúng trong thân phương thức — một lệnh <c>DateTime.UtcNow</c> lọt vào tầng quyết định
/// sẽ không hiện ra ở bất kỳ chữ ký nào.
///
/// Có xử lý lớp do trình biên dịch sinh (máy trạng thái <c>async</c>, closure của lambda):
/// chúng được quy về lớp bao ngoài, nếu không thì mọi phương thức <c>async</c> sẽ vô hình
/// trước bộ gác này — và phần lớn mã của engine là async.
/// </remarks>
public static class IlScanner
{
    private static readonly Dictionary<short, OperandType> OperandTypes = BuildOperandTable();

    public static IReadOnlyList<CallSite> ScanCalls(Assembly assembly, Func<string, bool> namespaceFilter)
    {
        using var stream = File.OpenRead(assembly.Location);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();

        var results = new List<CallSite>();

        foreach (var typeHandle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(typeHandle);
            var (ns, ownerName) = ResolveOwner(md, type);
            if (!namespaceFilter(ns)) continue;

            foreach (var methodHandle in type.GetMethods())
            {
                var method = md.GetMethodDefinition(methodHandle);
                if (method.RelativeVirtualAddress == 0) continue;

                var body = pe.GetMethodBody(method.RelativeVirtualAddress);
                var il = body.GetILBytes();
                if (il is null) continue;

                var methodName = md.GetString(method.Name);
                foreach (var token in DecodeCallTokens(il))
                {
                    var target = ResolveMember(md, token);
                    if (target is not null)
                        results.Add(new CallSite(ownerName, methodName, target.Value.Type, target.Value.Member));
                }
            }
        }

        return results;
    }

    /// <summary>Quy một lớp do trình biên dịch sinh về lớp bao ngoài cùng.</summary>
    private static (string Namespace, string TypeName) ResolveOwner(MetadataReader md, TypeDefinition type)
    {
        var current = type;
        while (current.IsNested)
        {
            current = md.GetTypeDefinition(current.GetDeclaringType());
        }
        return (md.GetString(current.Namespace), md.GetString(current.Name));
    }

    /// <summary>
    /// Đi qua từng lệnh IL và thu token của <c>call</c> / <c>callvirt</c> / <c>newobj</c>.
    /// Phải giải mã đúng độ dài toán hạng — quét thô theo byte mã lệnh sẽ bắt nhầm
    /// các byte nằm bên trong toán hạng.
    /// </summary>
    private static IEnumerable<int> DecodeCallTokens(byte[] il)
    {
        var i = 0;
        while (i < il.Length)
        {
            short opcode = il[i];
            i++;

            if (opcode == 0xFE)
            {
                if (i >= il.Length) yield break;
                opcode = (short)(0xFE00 | il[i]);
                i++;
            }

            if (!OperandTypes.TryGetValue(opcode, out var operand)) yield break;   // IL lạ → dừng an toàn

            var isCall = opcode is 0x28 or 0x6F or 0x73;   // call, callvirt, newobj
            if (isCall && i + 4 <= il.Length)
                yield return BitConverter.ToInt32(il, i);

            var skip = operand switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI
                    or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString
                    or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => SwitchSize(il, i),
                _ => -1,
            };

            if (skip < 0) yield break;
            i += skip;
        }
    }

    private static int SwitchSize(byte[] il, int at)
    {
        if (at + 4 > il.Length) return -1;
        var count = BitConverter.ToUInt32(il, at);
        return count > (uint)il.Length ? -1 : 4 + (int)count * 4;
    }

    private static (string Type, string Member)? ResolveMember(MetadataReader md, int token)
    {
        var handle = MetadataTokens.EntityHandle(token);
        return handle.Kind switch
        {
            HandleKind.MemberReference => FromMemberRef(md, (MemberReferenceHandle)handle),
            HandleKind.MethodDefinition => FromMethodDef(md, (MethodDefinitionHandle)handle),
            HandleKind.MethodSpecification => ResolveMember(md,
                MetadataTokens.GetToken(md.GetMethodSpecification((MethodSpecificationHandle)handle).Method)),
            _ => null,
        };
    }

    private static (string, string)? FromMemberRef(MetadataReader md, MemberReferenceHandle handle)
    {
        var mr = md.GetMemberReference(handle);
        var name = md.GetString(mr.Name);

        return mr.Parent.Kind switch
        {
            HandleKind.TypeReference => (FullName(md, md.GetTypeReference((TypeReferenceHandle)mr.Parent)), name),
            HandleKind.TypeDefinition => (FullName(md, md.GetTypeDefinition((TypeDefinitionHandle)mr.Parent)), name),
            _ => ("<unknown>", name),
        };
    }

    private static (string, string)? FromMethodDef(MetadataReader md, MethodDefinitionHandle handle)
    {
        var m = md.GetMethodDefinition(handle);
        return (FullName(md, md.GetTypeDefinition(m.GetDeclaringType())), md.GetString(m.Name));
    }

    private static string FullName(MetadataReader md, TypeReference t)
    {
        var ns = md.GetString(t.Namespace);
        var name = md.GetString(t.Name);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    private static string FullName(MetadataReader md, TypeDefinition t)
    {
        var ns = md.GetString(t.Namespace);
        var name = md.GetString(t.Name);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    private static Dictionary<short, OperandType> BuildOperandTable()
    {
        var table = new Dictionary<short, OperandType>();
        foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (f.GetValue(null) is OpCode op) table[op.Value] = op.OperandType;
        }
        return table;
    }

    /// <summary>Số lớp quét được — dùng để khẳng định bộ gác không "xanh vì không quét gì".</summary>
    public static int CountTypes(Assembly assembly, Func<string, bool> namespaceFilter)
    {
        using var stream = File.OpenRead(assembly.Location);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(handle);
            if (type.IsNested) continue;

            var ns = md.GetString(type.Namespace);
            if (namespaceFilter(ns)) seen.Add($"{ns}.{md.GetString(type.Name)}");
        }
        return seen.Count;
    }

    public static ImmutableArray<string> Empty => ImmutableArray<string>.Empty;
}
