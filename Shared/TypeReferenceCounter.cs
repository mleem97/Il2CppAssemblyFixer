using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DN = dnlib.DotNet;
using DNEmit = dnlib.DotNet.Emit;

namespace Il2CppAssemblyFixer.Shared;

internal static class TypeReferenceCounter
{
    private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }

    public static Dictionary<DN.TypeDef, int> Build(DN.ModuleDefMD module)
    {
        var counts = new Dictionary<DN.TypeDef, int>(new ReferenceComparer<DN.TypeDef>());
        foreach (DN.TypeDef type in module.GetTypes())
            counts[type] = 0;

        foreach (DN.TypeDef type in module.GetTypes())
            ScanType(type, counts);

        return counts;
    }

    private static void Increment(DN.TypeDef? type, Dictionary<DN.TypeDef, int> counts)
    {
        if (type != null && counts.ContainsKey(type))
            counts[type]++;
    }

    private static void ScanType(DN.TypeDef type, Dictionary<DN.TypeDef, int> counts)
    {
        ScanTypeRef(type.BaseType, counts);
        ScanInterfaces(type, counts);
        ScanCustomAttributes(type.CustomAttributes, counts);
        ScanFields(type, counts);
        ScanMethods(type, counts);
        ScanEvents(type, counts);
    }

    private static void ScanInterfaces(DN.TypeDef type, Dictionary<DN.TypeDef, int> counts)
    {
        foreach (DN.InterfaceImpl iface in type.Interfaces)
            ScanTypeRef(iface.Interface, counts);
    }

    private static void ScanCustomAttributes(IEnumerable<DN.CustomAttribute> attributes, Dictionary<DN.TypeDef, int> counts)
    {
        foreach (DN.CustomAttribute customAttribute in attributes)
            if (customAttribute.Constructor?.DeclaringType is DN.TypeDef attributeType)
                Increment(attributeType, counts);
    }

    private static void ScanFields(DN.TypeDef type, Dictionary<DN.TypeDef, int> counts)
    {
        foreach (DN.FieldDef field in type.Fields)
            ScanFieldSig(field.FieldSig, counts);
    }

    private static void ScanMethods(DN.TypeDef type, Dictionary<DN.TypeDef, int> counts)
    {
        foreach (DN.MethodDef method in type.Methods)
        {
            ScanMethodSig(method.MethodSig, counts);
            ScanCustomAttributes(method.CustomAttributes, counts);
            ScanMethodBody(method, counts);
        }
    }

    private static void ScanEvents(DN.TypeDef type, Dictionary<DN.TypeDef, int> counts)
    {
        foreach (DN.EventDef eventDef in type.Events)
            ScanTypeRef(eventDef.EventType, counts);
    }

    private static void ScanTypeRef(DN.ITypeDefOrRef? typeRef, Dictionary<DN.TypeDef, int> counts)
    {
        switch (typeRef)
        {
            case DN.TypeDef typeDef:
                Increment(typeDef, counts);
                break;
            case DN.TypeSpec typeSpec:
                ScanTypeSig(typeSpec.TypeSig, counts);
                break;
            default:
                break;
        }
    }

    private static void ScanTypeSig(DN.TypeSig? sig, Dictionary<DN.TypeDef, int> counts)
    {
        while (sig != null)
        {
            switch (sig)
            {
                case DN.TypeDefOrRefSig typeDefOrRefSig:
                    ScanTypeRef(typeDefOrRefSig.TypeDefOrRef, counts);
                    return;
                case DN.GenericInstSig genericInstSig:
                    ScanTypeSig(genericInstSig.GenericType, counts);
                    foreach (DN.TypeSig argument in genericInstSig.GenericArguments)
                        ScanTypeSig(argument, counts);
                    return;
                case DN.FnPtrSig fnPtrSig:
                    ScanMethodSig(fnPtrSig.MethodSig, counts);
                    return;
                case DN.CModOptSig cModOptSig:
                    sig = cModOptSig.Next;
                    continue;
                case DN.CModReqdSig cModReqdSig:
                    sig = cModReqdSig.Next;
                    continue;
                case DN.PinnedSig pinnedSig:
                    sig = pinnedSig.Next;
                    continue;
                case DN.PtrSig ptrSig:
                    sig = ptrSig.Next;
                    continue;
                case DN.ByRefSig byRefSig:
                    sig = byRefSig.Next;
                    continue;
                case DN.SZArraySig szArraySig:
                    sig = szArraySig.Next;
                    continue;
                case DN.ArraySig arraySig:
                    sig = arraySig.Next;
                    continue;
                case DN.SentinelSig sentinelSig:
                    sig = sentinelSig.Next;
                    continue;
                default:
                    return;
            }
        }
    }

    private static void ScanMethodSig(DN.MethodSig? methodSig, Dictionary<DN.TypeDef, int> counts)
    {
        if (methodSig == null)
            return;

        ScanTypeSig(methodSig.RetType, counts);
        foreach (DN.TypeSig parameter in methodSig.Params)
            ScanTypeSig(parameter, counts);
    }

    private static void ScanFieldSig(DN.FieldSig? fieldSig, Dictionary<DN.TypeDef, int> counts)
    {
        if (fieldSig != null)
            ScanTypeSig(fieldSig.Type, counts);
    }

    private static void ScanMethodBody(DN.MethodDef method, Dictionary<DN.TypeDef, int> counts)
    {
        DNEmit.CilBody body = method.Body;
        if (body == null)
            return;

        foreach (DNEmit.Local local in body.Variables)
            ScanTypeSig(local.Type, counts);

        foreach (DNEmit.Instruction instruction in body.Instructions)
            ScanInstructionOperand(instruction.Operand, counts);
    }

    private static void ScanInstructionOperand(object? operand, Dictionary<DN.TypeDef, int> counts)
    {
        switch (operand)
        {
            case DN.ITypeDefOrRef typeDefOrRef:
                ScanTypeRef(typeDefOrRef, counts);
                break;
            case DN.MemberRef memberRef:
                ScanTypeRef(memberRef.DeclaringType, counts);
                break;
            case DN.IMethodDefOrRef methodDefOrRef:
                ScanTypeRef(methodDefOrRef.DeclaringType, counts);
                break;
            case DN.IField fieldRef:
                ScanTypeRef(fieldRef.DeclaringType, counts);
                break;
            default:
                break;
        }
    }
}
