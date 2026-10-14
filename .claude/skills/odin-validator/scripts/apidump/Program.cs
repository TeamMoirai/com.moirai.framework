using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// 装机 dll 的成员级真相：类型/字段/方法（含返回与参数类型）/属性名与访问器。
// 已知缺陷：属性那一行的"类型"列打印的是签名对象而非类型名，要看属性类型请改看它的 getter 方法行。
// usage: apidump <AssembliesDir> <FullNameSubstring> [...]
sealed class Prov : ISignatureTypeProvider<string, object>
{
    public string GetArrayType(string e, ArrayShape s) => e + "[…]";
    public string GetByReferenceType(string e) => "ref " + e;
    public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
    public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
    public string GetGenericMethodParameter(object gc, int i) => "M" + i;
    public string GetGenericTypeParameter(object gc, int i) => "T" + i;
    public string GetModifiedType(string mod, string un, bool isRequired) => un;
    public string GetPinnedType(string e) => e;
    public string GetPointerType(string e) => e + "*";
    public string GetPrimitiveType(PrimitiveTypeCode c) => c switch
    {
        PrimitiveTypeCode.Void => "void",
        PrimitiveTypeCode.Boolean => "bool",
        PrimitiveTypeCode.Int32 => "int",
        PrimitiveTypeCode.Int64 => "long",
        PrimitiveTypeCode.Single => "float",
        PrimitiveTypeCode.Double => "double",
        PrimitiveTypeCode.String => "string",
        PrimitiveTypeCode.Object => "object",
        PrimitiveTypeCode.Char => "char",
        _ => c.ToString().ToLowerInvariant(),
    };
    public string GetSZArrayType(string e) => e + "[]";
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k)
    {
        var t = r.GetTypeDefinition(h);
        var ns = r.GetString(t.Namespace);
        return (string.IsNullOrEmpty(ns) ? "" : ns + ".") + r.GetString(t.Name);
    }
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k)
    {
        var t = r.GetTypeReference(h);
        var ns = r.GetString(t.Namespace);
        return (string.IsNullOrEmpty(ns) ? "" : ns + ".") + r.GetString(t.Name);
    }
    public string GetTypeFromSpecification(MetadataReader r, object gc, TypeSpecificationHandle h, byte k)
        => r.GetTypeSpecification(h).DecodeSignature(this, gc);
}

static class App
{
    static readonly Prov P = new();

    static string Tv(TypeAttributes a) => (a & TypeAttributes.VisibilityMask) switch
    {
        TypeAttributes.NotPublic => "internal",
        TypeAttributes.Public => "public",
        _ => (a & TypeAttributes.VisibilityMask).ToString().ToLowerInvariant(),
    };
    static string Acc(int v) => v switch
    {
        0 => "private", 1 => "privatescope", 2 => "private protected", 3 => "internal",
        4 => "protected", 5 => "protected internal", 6 => "public", _ => "?" + v,
    };
    static string Mv(MethodAttributes a) => Acc((int)(a & MethodAttributes.MemberAccessMask));
    static string Fv(FieldAttributes a) => Acc((int)(a & FieldAttributes.FieldAccessMask));
    static string Ref(MetadataReader r, EntityHandle h) => h.Kind switch
    {
        HandleKind.TypeReference => P.GetTypeFromReference(r, (TypeReferenceHandle)h, 0),
        HandleKind.TypeDefinition => P.GetTypeFromDefinition(r, (TypeDefinitionHandle)h, 0),
        HandleKind.TypeSpecification => "<spec>",
        _ => "-",
    };

    static void Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: apidump <dir> <substr> ..."); return; }
        foreach (var dll in Directory.GetFiles(args[0], "*.dll").OrderBy(x => x, StringComparer.Ordinal))
        {
            using var fs = File.OpenRead(dll);
            using var pe = new PEReader(fs);
            if (!pe.HasMetadata) continue;
            var r = pe.GetMetadataReader();
            var head = false;
            foreach (var th in r.TypeDefinitions)
            {
                var td = r.GetTypeDefinition(th);
                var tns = r.GetString(td.Namespace);
                var full = (string.IsNullOrEmpty(tns) ? "" : tns + ".") + r.GetString(td.Name);
                if (!args.Skip(1).Any(s => full.Contains(s, StringComparison.Ordinal))) continue;
                if (!head) { Console.WriteLine("\n### " + Path.GetFileName(dll)); head = true; }
                var ta = td.Attributes;
                var shape = ta.HasFlag(TypeAttributes.Interface) ? "interface"
                    : ta.HasFlag(TypeAttributes.Abstract) ? "abstract"
                    : ta.HasFlag(TypeAttributes.Sealed) ? "sealed" : "class";
                Console.WriteLine($"{full}   [{Tv(ta)} {shape} : {(td.BaseType.IsNil ? "-" : Ref(r, td.BaseType))}]");
                foreach (var ih in td.GetInterfaceImplementations())
                    Console.WriteLine("      implements " + Ref(r, r.GetInterfaceImplementation(ih).Interface));
                foreach (var fh in td.GetFields())
                {
                    var f = r.GetFieldDefinition(fh);
                    Console.WriteLine($"      F {Fv(f.Attributes)}{(f.Attributes.HasFlag(FieldAttributes.Static) ? " static" : "")} "
                        + $"{f.DecodeSignature(P, null)} {r.GetString(f.Name)}");
                }
                foreach (var ph in td.GetProperties())
                {
                    var pr = r.GetPropertyDefinition(ph);
                    var accs = pr.GetAccessors();
                    MethodDefinition? get = accs.Getter.IsNil ? null : r.GetMethodDefinition(accs.Getter);
                    MethodDefinition? set = accs.Setter.IsNil ? null : r.GetMethodDefinition(accs.Setter);
                    if (get is null && set is null) { Console.WriteLine($"      P ? {r.GetString(pr.Name)} (no accessors)"); continue; }
                    var acc = (get ?? set).Value;
                    var virt = acc.Attributes.HasFlag(MethodAttributes.Virtual) ? " virtual" : "";
                    var abst = acc.Attributes.HasFlag(MethodAttributes.Abstract) ? " abstract" : "";
                    Console.WriteLine($"      P {Mv(acc.Attributes)}{virt}{abst} {pr.DecodeSignature(P, null)} {r.GetString(pr.Name)}"
                        + (!accs.Getter.IsNil ? " {get}" : "") + (!accs.Setter.IsNil ? " {set}" : ""));
                }
                foreach (var mh in td.GetMethods())
                {
                    var m = r.GetMethodDefinition(mh);
                    if (m.Attributes.HasFlag(MethodAttributes.SpecialName) || m.Attributes.HasFlag(MethodAttributes.RTSpecialName)) continue;
                    var sig = m.DecodeSignature(P, null);
                    var pars = m.GetParameters().Select(h => r.GetParameter(h)).ToList();
                    var list = new List<string>();
                    for (int i = 0; i < sig.ParameterTypes.Length; i++)
                    {
                        var nm = pars.FirstOrDefault(x => x.SequenceNumber == i + 1);
                        list.Add(sig.ParameterTypes[i] + " " + (nm.SequenceNumber == 0 || nm.Name.IsNil ? "?" : r.GetString(nm.Name)));
                    }
                    var ma = m.Attributes;
                    var mods = (ma.HasFlag(MethodAttributes.Static) ? " static" : "")
                        + (ma.HasFlag(MethodAttributes.Abstract) ? " abstract" : ma.HasFlag(MethodAttributes.Virtual) ? " virtual" : "");
                    Console.WriteLine($"      M {Mv(ma)}{mods} {sig.ReturnType} {r.GetString(m.Name)}({string.Join(", ", list)})");
                }
            }
        }
    }
}
