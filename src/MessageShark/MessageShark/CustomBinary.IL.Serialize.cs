﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;

namespace MessageShark {
    public static partial class CustomBinary {
        static void WriteSerializerArray(TypeBuilder typeBuilder, ILGenerator il, Type type, MethodInfo valueMethod) {
            var itemType = type.GetElementType();
            var itemLocal = il.DeclareLocal(itemType);
            var listLocal = il.DeclareLocal(type);
            var indexLocal = il.DeclareLocal(typeof(int));
            var startLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_2);
            if(valueMethod != null) il.Emit(OpCodes.Callvirt, valueMethod);
            il.Emit(OpCodes.Stloc, listLocal.LocalIndex);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc_S, indexLocal.LocalIndex);
            il.Emit(OpCodes.Br, startLabel);
            il.MarkLabel(endLabel);
            il.Emit(OpCodes.Ldloc, listLocal.LocalIndex);
            il.Emit(OpCodes.Ldloc_S, indexLocal.LocalIndex);
            il.Emit(OpCodes.Ldelem, itemType);
            il.Emit(OpCodes.Stloc, itemLocal.LocalIndex);
            if (itemType.IsComplexType()) {
                WriteSerializerCallClassMethod(typeBuilder, il, itemType, OpCodes.Ldloc, itemLocal.LocalIndex, 1, null,
                    needClassHeader: false);
            } else {
                WriteSerializerBytesToStream(il, itemType, OpCodes.Ldloc, itemLocal.LocalIndex, 1, null, isTargetCollection: true);
            }
            il.Emit(OpCodes.Ldloc_S, indexLocal.LocalIndex);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc_S, indexLocal.LocalIndex);
            il.MarkLabel(startLabel);
            il.Emit(OpCodes.Ldloc_S, indexLocal.LocalIndex);
            il.Emit(OpCodes.Ldloc, listLocal.LocalIndex);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Blt, endLabel);
        }

        static void WriteSerializerList(TypeBuilder typeBuilder, ILGenerator il, Type type, MethodInfo valueMethod) {
            var listType = type.GetGenericArguments()[0];
            var enumeratorType = GenericListEnumerator.MakeGenericType(listType);
            var enumeratorLocal = il.DeclareLocal(enumeratorType);
            var entryLocal = il.DeclareLocal(listType);
            var startEnumeratorLabel = il.DefineLabel();
            var moveNextLabel = il.DefineLabel();
            var endEnumeratorLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_2);
            if (valueMethod != null) il.Emit(OpCodes.Callvirt, valueMethod);
            il.Emit(OpCodes.Callvirt,
                GenericListType.MakeGenericType(listType)
                .GetMethod("GetEnumerator"));
            il.Emit(OpCodes.Stloc_S, enumeratorLocal.LocalIndex);
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Br, startEnumeratorLabel);
            il.MarkLabel(moveNextLabel);
            il.Emit(OpCodes.Ldloca_S, enumeratorLocal.LocalIndex);
            il.Emit(OpCodes.Call,
                enumeratorLocal.LocalType.GetProperty("Current")
                .GetGetMethod());
            il.Emit(OpCodes.Stloc, entryLocal.LocalIndex);
            if (listType.IsComplexType()) {
                WriteSerializerCallClassMethod(typeBuilder, il, listType, OpCodes.Ldloc, entryLocal.LocalIndex, 1, null, needClassHeader: false);
            } else {
                WriteSerializerBytesToStream(il, listType, OpCodes.Ldloc, entryLocal.LocalIndex, 1, null, isTargetCollection: true);
            }
            il.MarkLabel(startEnumeratorLabel);
            il.Emit(OpCodes.Ldloca_S, enumeratorLocal.LocalIndex);
            il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext", MethodBinding));
            il.Emit(OpCodes.Brtrue, moveNextLabel);
            il.Emit(OpCodes.Leave, endEnumeratorLabel);
            il.BeginFinallyBlock();
            il.Emit(OpCodes.Ldloca_S, enumeratorLocal.LocalIndex);
            il.Emit(OpCodes.Constrained, enumeratorLocal.LocalType);
            il.Emit(OpCodes.Callvirt, IDisposableDisposeMethod);
            il.EndExceptionBlock();
            il.MarkLabel(endEnumeratorLabel);
        }

        static void WriteSerializerDictionary(TypeBuilder typeBuilder, ILGenerator il, Type type, MethodInfo getValueMethod) {
            var arguments = type.GetGenericArguments();
            var keyType = arguments[0];
            var valueType = arguments[1];
            var keyValuePairType = GenericKeyValuePairType.MakeGenericType(keyType, valueType);
            var enumeratorType = GenericDictionaryEnumerator.MakeGenericType(keyType, valueType);
            var enumeratorLocal = il.DeclareLocal(enumeratorType);
            var entryLocal = il.DeclareLocal(keyValuePairType);
            var startEnumeratorLabel = il.DefineLabel();
            var moveNextLabel = il.DefineLabel();
            var endEnumeratorLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_2);
            if (getValueMethod != null) il.Emit(OpCodes.Callvirt, getValueMethod);
            il.Emit(OpCodes.Callvirt,
                GenericDictType.MakeGenericType(keyType, valueType)
                .GetMethod("GetEnumerator"));
            il.Emit(OpCodes.Stloc_S, enumeratorLocal.LocalIndex);
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Br, startEnumeratorLabel);
            il.MarkLabel(moveNextLabel);
            il.Emit(OpCodes.Ldloca_S, enumeratorLocal.LocalIndex);
            il.Emit(OpCodes.Call,
                enumeratorLocal.LocalType.GetProperty("Current")
                .GetGetMethod());
            il.Emit(OpCodes.Stloc, entryLocal.LocalIndex);
            if (keyType.IsComplexType()) {
                var keyMethod = keyValuePairType.GetProperty("Key").GetGetMethod();
                WriteSerializerCallClassMethod(typeBuilder, il, keyType, OpCodes.Ldloca_S, entryLocal.LocalIndex, 1, keyMethod,
                needClassHeader: false);
            } else {
                var keyMethod = keyValuePairType.GetProperty("Key").GetGetMethod();
                WriteSerializerBytesToStream(il, keyType, OpCodes.Ldloca_S, entryLocal.LocalIndex, 1, keyMethod, isTargetCollection: true);
            }
            if (valueType.IsComplexType()) {
                var valueMethod = keyValuePairType.GetProperty("Value").GetGetMethod();
                WriteSerializerCallClassMethod(typeBuilder, il, valueType, OpCodes.Ldloca_S, entryLocal.LocalIndex, 2, valueMethod,
                needClassHeader: false);
            } else {
                var valueMethod = keyValuePairType.GetProperty("Value").GetGetMethod();
                WriteSerializerBytesToStream(il, valueType, OpCodes.Ldloca_S, entryLocal.LocalIndex, 2, valueMethod, isTargetCollection: true);
            }
            il.MarkLabel(startEnumeratorLabel);
            il.Emit(OpCodes.Ldloca_S, enumeratorLocal.LocalIndex);
            il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext", MethodBinding));
            il.Emit(OpCodes.Brtrue, moveNextLabel);
            il.Emit(OpCodes.Leave, endEnumeratorLabel);
            il.BeginFinallyBlock();
            il.Emit(OpCodes.Ldloca_S, enumeratorLocal.LocalIndex);
            il.Emit(OpCodes.Constrained, enumeratorLocal.LocalType);
            il.Emit(OpCodes.Callvirt, IDisposableDisposeMethod);
            il.EndExceptionBlock();
            il.MarkLabel(endEnumeratorLabel);
        }

        static void WriteSerializerBytesToStream(ILGenerator il, Type type, OpCode valueOpCode, int valueLocalIndex, int tag, 
            MethodInfo valueMethod, bool isTargetCollection) {
            var isTypeEnum = type.IsEnum;
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(valueOpCode, valueLocalIndex);
            if (valueMethod != null) il.Emit(OpCodes.Callvirt, valueMethod);
            if (isTypeEnum) il.Emit(OpCodes.Box, type);
            il.Emit(OpCodes.Ldc_I4, tag);
            if (isTypeEnum) type = EnumType;
            il.Emit(OpCodes.Ldc_I4, isTargetCollection ? 1 : 0);
            il.Emit(OpCodes.Call, PrimitiveWriterMethods[type]);
        }

        static MethodBuilder GenerateSerializerClass(TypeBuilder typeBuilder, Type objType, bool isEntryPoint = false) {
            MethodBuilder method;
            if (WriterMethodBuilders.TryGetValue(objType, out method)) return method;
            var methodName = String.Intern("Write") + objType.Name;
            method = typeBuilder.DefineMethod(methodName, MethodAttribute,
                typeof(void), new[] { typeof(CustomBuffer),  objType, typeof(int), typeof(bool) });

            var methodIL = method.GetILGenerator();

            WriteSerializerClass(typeBuilder, methodIL, objType, tag: 0, valueMethod: null, callerType: objType, isEntryPoint: isEntryPoint);
            
            methodIL.Emit(OpCodes.Ret);
            return (WriterMethodBuilders[objType] = method);
        }

        static void WriteSerializerClass(TypeBuilder typeBuilder, ILGenerator il, Type type, int tag, MethodInfo valueMethod,
            Type callerType = null, bool isEntryPoint = false) {
            var isDict = type.IsDictionaryType();
            var isList = type.IsListType();
            var isClass = !isDict && !isList;
            var isDefaultLabel = il.DefineLabel();
            
            il.Emit(OpCodes.Ldarg_2);
            if (valueMethod != null) il.Emit(OpCodes.Callvirt, valueMethod);
            il.Emit(OpCodes.Brfalse, isDefaultLabel);
            
            if (type.IsClassType()) {
                WriteSerializerProperties(typeBuilder, il, type, callerType, isEntryPoint);
            } else {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_2);
                if (valueMethod != null) il.Emit(OpCodes.Callvirt, valueMethod);
                il.Emit(OpCodes.Castclass, ICollectionType);
                il.Emit(OpCodes.Ldc_I4, tag);
                il.Emit(OpCodes.Call, WriteCollectionHeaderMethod);
                if (isDict)
                    WriteSerializerDictionary(typeBuilder, il, type, valueMethod);
                else if (isList) {
                    if (type.IsArray) WriteSerializerArray(typeBuilder, il, type, valueMethod);
                    else WriteSerializerList(typeBuilder, il, type, valueMethod);
                }
            }
            il.MarkLabel(isDefaultLabel);
        }

        static void WriteSerializerCallClassMethod(TypeBuilder typeBuilder, ILGenerator il, Type type,
            OpCode valueOpCode, int valueLocalIndex, int tag, MethodInfo valueMethod, bool needClassHeader) {
            var method = GenerateSerializerClass(typeBuilder, type);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(valueOpCode, valueLocalIndex);
            if (valueMethod != null) il.Emit(OpCodes.Callvirt, valueMethod);
            il.Emit(OpCodes.Ldc_I4, tag);
            il.Emit(OpCodes.Ldc_I4, needClassHeader ? 1 : 0);
            il.Emit(OpCodes.Call, method);
        }

        static void WriteSerializerProperties(TypeBuilder typeBuilder, ILGenerator il, Type type, 
            Type callerType, bool isEntryPoint, bool needClassHeader = true) {

            var props = GetTypeProperties(type);
            var tag = 1;

            if (!isEntryPoint) {
                var needClassHeaderLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldarg, 4);
                il.Emit(OpCodes.Brfalse, needClassHeaderLabel);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ldarg_3);
                il.Emit(OpCodes.Call, EncodeLengthMethod);
                il.Emit(OpCodes.Call, BufferStreamWriteBytesMethod);
                il.MarkLabel(needClassHeaderLabel);
            }

            if (TypeIDMapping.ContainsKey(type)) {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldtoken, type);
                il.Emit(OpCodes.Call, GetTypeFromHandleMethod);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Callvirt, GetTypeMethod);
                il.Emit(OpCodes.Call, WriteTypeIDForMethod);
            }

            foreach (var prop in props) {
                var propType = prop.PropertyType;
                var getMethod = prop.GetGetMethod();

                if (propType.IsComplexType()) {
                    if (propType.IsCollectionType())
                        WriteSerializerClass(typeBuilder, il, propType, tag, getMethod, callerType: callerType);
                    else {
                        var method = GenerateSerializerClass(typeBuilder, propType);
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(OpCodes.Ldarg_1);
                        il.Emit(OpCodes.Ldarg_2);
                        il.Emit(OpCodes.Callvirt, getMethod);
                        il.Emit(OpCodes.Ldc_I4, tag);
                        il.Emit(OpCodes.Ldc_I4, needClassHeader ? 1 : 0);
                        il.Emit(OpCodes.Call, method);
                    }
                } else {
                    var isTypeEnum = propType.IsEnum;
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldarg_2);
                    il.Emit(OpCodes.Callvirt, getMethod);
                    if (isTypeEnum) il.Emit(OpCodes.Box, propType);
                    il.Emit(OpCodes.Ldc_I4, tag);
                    if (isTypeEnum) propType = EnumType;
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Call, PrimitiveWriterMethods[propType]);
                }
                tag++;
            }
        }
    }
}