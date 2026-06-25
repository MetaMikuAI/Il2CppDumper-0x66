using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Il2CppDumper
{
    public class DummyAssemblyGenerator
    {
        public List<AssemblyDefinition> Assemblies = new();

        private readonly Il2CppExecutor executor;
        private readonly Metadata metadata;
        private readonly Il2Cpp il2Cpp;
        private readonly Dictionary<Il2CppTypeDefinition, TypeDefinition> typeDefinitionDic = new();
        private readonly Dictionary<(Il2CppGenericParameter, IGenericParameterProvider), GenericParameter> genericParameterDic = new();
        private readonly MethodDefinition attributeAttribute;
        private readonly TypeReference stringType;
        private readonly TypeSystem typeSystem;
        private readonly Dictionary<int, FieldDefinition> fieldDefinitionDic = new();
        private readonly Dictionary<int, PropertyDefinition> propertyDefinitionDic = new();
        private readonly Dictionary<int, MethodDefinition> methodDefinitionDic = new();

        public DummyAssemblyGenerator(Il2CppExecutor il2CppExecutor, bool addToken)
        {
            executor = il2CppExecutor;
            metadata = il2CppExecutor.metadata;
            il2Cpp = il2CppExecutor.il2Cpp;

            //Il2CppDummyDll
            var il2CppDummyDll = AssemblyDefinition.ReadAssembly(new MemoryStream(Resource1.Il2CppDummyDll));
            Assemblies.Add(il2CppDummyDll);
            var dummyMD = il2CppDummyDll.MainModule;
            var addressAttribute = dummyMD.Types.First(x => x.Name == "AddressAttribute").Methods[0];
            var fieldOffsetAttribute = dummyMD.Types.First(x => x.Name == "FieldOffsetAttribute").Methods[0];
            attributeAttribute = dummyMD.Types.First(x => x.Name == "AttributeAttribute").Methods[0];
            var metadataOffsetAttribute = dummyMD.Types.First(x => x.Name == "MetadataOffsetAttribute").Methods[0];
            var tokenAttribute = dummyMD.Types.First(x => x.Name == "TokenAttribute").Methods[0];
            stringType = dummyMD.TypeSystem.String;
            typeSystem = dummyMD.TypeSystem;

            var resolver = new MyAssemblyResolver();
            var moduleParameters = new ModuleParameters
            {
                Kind = ModuleKind.Dll,
                AssemblyResolver = resolver
            };
            resolver.Register(il2CppDummyDll);

            var parameterDefinitionDic = new Dictionary<int, ParameterDefinition>();
            var eventDefinitionDic = new Dictionary<int, EventDefinition>();

            //创建程序集，同时创建所有类
            foreach (var imageDef in metadata.imageDefs)
            {
                var imageName = metadata.GetStringFromIndex(imageDef.nameIndex);
                var assemblyName = Path.GetFileNameWithoutExtension(imageName);
                var vers = new Version(3, 7, 1, 6);
                if (imageDef.assemblyIndex >= 0 && imageDef.assemblyIndex < metadata.assemblyDefs.Length)
                {
                    var aname = metadata.assemblyDefs[imageDef.assemblyIndex].aname;
                    var parsedAssemblyName = metadata.GetStringFromIndex(aname.nameIndex);
                    if (!string.IsNullOrWhiteSpace(parsedAssemblyName))
                        assemblyName = parsedAssemblyName;
                    if (aname.major >= 0 && aname.minor >= 0 && aname.build >= 0 && aname.revision >= 0)
                        vers = new Version(aname.major, aname.minor, aname.build, aname.revision);
                }
                if (string.IsNullOrWhiteSpace(assemblyName))
                    assemblyName = $"Assembly_{Assemblies.Count}";
                var assemblyNameDef = new AssemblyNameDefinition(assemblyName, vers);
                /*assemblyNameDef.Culture = metadata.GetStringFromIndex(aname.cultureIndex);
                assemblyNameDef.PublicKey = Encoding.UTF8.GetBytes(metadata.GetStringFromIndex(aname.publicKeyIndex));
                assemblyNameDef.HashAlgorithm = (AssemblyHashAlgorithm)aname.hash_alg;
                assemblyNameDef.Attributes = (AssemblyAttributes)aname.flags;
                assemblyNameDef.PublicKeyToken = aname.public_key_token;*/
                var assemblyDefinition = AssemblyDefinition.CreateAssembly(assemblyNameDef, imageName, moduleParameters);
                resolver.Register(assemblyDefinition);
                Assemblies.Add(assemblyDefinition);
                var moduleDefinition = assemblyDefinition.MainModule;
                moduleDefinition.Types.Clear();//清除自动创建的<Module>类
                var typeEnd = imageDef.typeStart + imageDef.typeCount;
                for (var index = imageDef.typeStart; index < typeEnd; ++index)
                {
                    var typeDef = metadata.typeDefs[index];
                    var namespaceName = metadata.GetStringFromIndex(typeDef.namespaceIndex);
                    var typeName = metadata.GetStringFromIndex(typeDef.nameIndex);
                    if (string.IsNullOrWhiteSpace(typeName))
                        typeName = $"Type_{index}";
                    var typeDefinition = new TypeDefinition(namespaceName, typeName, (TypeAttributes)typeDef.flags);
                    typeDefinitionDic.Add(typeDef, typeDefinition);
                    if (typeDef.declaringTypeIndex == -1 || metadata.IsAceMetadataLayout)
                    {
                        moduleDefinition.Types.Add(typeDefinition);
                    }
                }
            }
            foreach (var imageDef in metadata.imageDefs)
            {
                var typeEnd = imageDef.typeStart + imageDef.typeCount;
                for (var index = imageDef.typeStart; index < typeEnd; ++index)
                {
                    var typeDef = metadata.typeDefs[index];
                    var typeDefinition = typeDefinitionDic[typeDef];

                    //nestedtype
                    if (typeDef.nested_type_count > 0 && typeDef.nestedTypesStart >= 0)
                    {
                        var nestedEnd = Math.Min(metadata.nestedTypeIndices.Length, typeDef.nestedTypesStart + typeDef.nested_type_count);
                        for (int i = typeDef.nestedTypesStart; i < nestedEnd; i++)
                        {
                            var nestedIndex = metadata.nestedTypeIndices[i];
                            if (nestedIndex < 0 || nestedIndex >= metadata.typeDefs.Length)
                                continue;
                            var nestedTypeDef = metadata.typeDefs[nestedIndex];
                            if (typeDefinitionDic.TryGetValue(nestedTypeDef, out var nestedTypeDefinition))
                            {
                                typeDefinition.NestedTypes.Add(nestedTypeDefinition);
                            }
                        }
                    }
                }
            }
            //提前处理
            foreach (var imageDef in metadata.imageDefs)
            {
                var typeEnd = imageDef.typeStart + imageDef.typeCount;
                for (var index = imageDef.typeStart; index < typeEnd; ++index)
                {
                    var typeDef = metadata.typeDefs[index];
                    var typeDefinition = typeDefinitionDic[typeDef];

                    if (addToken)
                    {
                        var customTokenAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(tokenAttribute));
                        customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{typeDef.token:X}")));
                        typeDefinition.CustomAttributes.Add(customTokenAttribute);
                    }

                    //genericParameter
                    if (typeDef.genericContainerIndex >= 0 && typeDef.genericContainerIndex < metadata.genericContainers.Length)
                    {
                        var genericContainer = metadata.genericContainers[typeDef.genericContainerIndex];
                        for (int i = 0; i < genericContainer.type_argc; i++)
                        {
                            var genericParameterIndex = genericContainer.genericParameterStart + i;
                            if (genericParameterIndex < 0 || genericParameterIndex >= metadata.genericParameters.Length)
                                continue;
                            var param = metadata.genericParameters[genericParameterIndex];
                            var genericParameter = CreateGenericParameter(param, typeDefinition);
                            typeDefinition.GenericParameters.Add(genericParameter);
                        }
                    }

                    //parent
                    if (typeDef.parentIndex >= 0 && typeDef.parentIndex < il2Cpp.types.Length)
                    {
                        var parentType = il2Cpp.types[typeDef.parentIndex];
                        var parentTypeRef = GetTypeReference(typeDefinition, parentType);
                        typeDefinition.BaseType = parentTypeRef;
                    }

                    //interfaces
                    if (typeDef.interfaces_count > 0 && typeDef.interfacesStart >= 0)
                    {
                        var interfacesEnd = Math.Min(metadata.interfaceIndices.Length, typeDef.interfacesStart + typeDef.interfaces_count);
                        for (int i = typeDef.interfacesStart; i < interfacesEnd; i++)
                        {
                            var interfaceIndex = metadata.interfaceIndices[i];
                            if (interfaceIndex < 0 || interfaceIndex >= il2Cpp.types.Length)
                                continue;
                            var interfaceType = il2Cpp.types[interfaceIndex];
                            var interfaceTypeRef = GetTypeReference(typeDefinition, interfaceType);
                            typeDefinition.Interfaces.Add(new InterfaceImplementation(interfaceTypeRef));
                        }
                    }
                }
            }
            //处理field, method, property等等
            foreach (var imageDef in metadata.imageDefs)
            {
                var imageName = metadata.GetStringFromIndex(imageDef.nameIndex);
                var typeEnd = imageDef.typeStart + imageDef.typeCount;
                for (int index = imageDef.typeStart; index < typeEnd; index++)
                {
                    var typeDef = metadata.typeDefs[index];
                    var typeDefinition = typeDefinitionDic[typeDef];

                    //field
                    var fieldEnd = typeDef.fieldStart >= 0 ? Math.Min(metadata.fieldDefs.Length, typeDef.fieldStart + typeDef.field_count) : 0;
                    for (var i = Math.Max(0, typeDef.fieldStart); i < fieldEnd; ++i)
                    {
                        var fieldDef = metadata.fieldDefs[i];
                        if (fieldDef.typeIndex < 0 || fieldDef.typeIndex >= il2Cpp.types.Length)
                            continue;
                        var fieldType = il2Cpp.types[fieldDef.typeIndex];
                        var fieldName = metadata.GetStringFromIndex(fieldDef.nameIndex);
                        var fieldTypeRef = GetTypeReference(typeDefinition, fieldType);
                        var fieldDefinition = new FieldDefinition(fieldName, (FieldAttributes)fieldType.attrs, fieldTypeRef);
                        typeDefinition.Fields.Add(fieldDefinition);
                        fieldDefinitionDic[i] = fieldDefinition;

                        if (addToken)
                        {
                            var customTokenAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(tokenAttribute));
                            customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{fieldDef.token:X}")));
                            fieldDefinition.CustomAttributes.Add(customTokenAttribute);
                        }

                        //fieldDefault
                        if (metadata.GetFieldDefaultValueFromIndex(i, out var fieldDefault) && fieldDefault.dataIndex != -1)
                        {
                            if (executor.TryGetDefaultValue(fieldDefault.typeIndex, fieldDefault.dataIndex, out var value) && CanWriteDefaultConstant(fieldTypeRef, value))
                            {
                                fieldDefinition.Constant = value;
                            }
                            else
                            {
                                var customAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(metadataOffsetAttribute));
                                var offset = new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{value:X}"));
                                customAttribute.Fields.Add(offset);
                                fieldDefinition.CustomAttributes.Add(customAttribute);
                            }
                        }
                        //fieldOffset
                        if (!fieldDefinition.IsLiteral)
                        {
                            var fieldOffset = il2Cpp.GetFieldOffsetFromIndex(index, i - typeDef.fieldStart, i, typeDefinition.IsValueType, fieldDefinition.IsStatic);
                            if (fieldOffset >= 0)
                            {
                                var customAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(fieldOffsetAttribute));
                                var offset = new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{fieldOffset:X}"));
                                customAttribute.Fields.Add(offset);
                                fieldDefinition.CustomAttributes.Add(customAttribute);
                            }
                        }
                    }
                    //method
                    var methodEnd = typeDef.methodStart >= 0 ? Math.Min(metadata.methodDefs.Length, typeDef.methodStart + typeDef.method_count) : 0;
                    for (var i = Math.Max(0, typeDef.methodStart); i < methodEnd; ++i)
                    {
                        var methodDef = metadata.methodDefs[i];
                        var methodName = metadata.GetStringFromIndex(methodDef.nameIndex);
                        var methodDefinition = new MethodDefinition(methodName, (MethodAttributes)methodDef.flags, typeDefinition.Module.ImportReference(typeSystem.Void))
                        {
                            ImplAttributes = (MethodImplAttributes)methodDef.iflags
                        };
                        typeDefinition.Methods.Add(methodDefinition);
                        //genericParameter
                        if (methodDef.genericContainerIndex >= 0 && methodDef.genericContainerIndex < metadata.genericContainers.Length)
                        {
                            var genericContainer = metadata.genericContainers[methodDef.genericContainerIndex];
                            for (int j = 0; j < genericContainer.type_argc; j++)
                            {
                                var genericParameterIndex = genericContainer.genericParameterStart + j;
                                if (genericParameterIndex < 0 || genericParameterIndex >= metadata.genericParameters.Length)
                                    continue;
                                var param = metadata.genericParameters[genericParameterIndex];
                                var genericParameter = CreateGenericParameter(param, methodDefinition);
                                methodDefinition.GenericParameters.Add(genericParameter);
                            }
                        }
                        var methodReturnType = methodDef.returnType >= 0 && methodDef.returnType < il2Cpp.types.Length ? il2Cpp.types[methodDef.returnType] : null;
                        var returnType = methodReturnType == null ? typeSystem.Object : GetTypeReferenceWithByRef(methodDefinition, methodReturnType);
                        methodDefinition.ReturnType = returnType;

                        if (addToken)
                        {
                            var customTokenAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(tokenAttribute));
                            customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{methodDef.token:X}")));
                            methodDefinition.CustomAttributes.Add(customTokenAttribute);
                        }

                        if (methodDefinition.HasBody && typeDefinition.BaseType?.FullName != "System.MulticastDelegate")
                        {
                            var ilprocessor = methodDefinition.Body.GetILProcessor();
                            if (returnType.FullName == "System.Void")
                            {
                                ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
                            }
                            else if (returnType.IsValueType)
                            {
                                var variable = new VariableDefinition(returnType);
                                methodDefinition.Body.Variables.Add(variable);
                                ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloca_S, variable));
                                ilprocessor.Append(ilprocessor.Create(OpCodes.Initobj, returnType));
                                ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_0));
                                ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
                            }
                            else
                            {
                                ilprocessor.Append(ilprocessor.Create(OpCodes.Ldnull));
                                ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
                            }
                        }
                        methodDefinitionDic[i] = methodDefinition;
                        //method parameter
                        for (var j = 0; j < methodDef.parameterCount; ++j)
                        {
                            var parameterIndex = methodDef.parameterStart + j;
                            if (parameterIndex < 0 || parameterIndex >= metadata.parameterDefs.Length)
                                continue;
                            var parameterDef = metadata.parameterDefs[parameterIndex];
                            if (parameterDef.typeIndex < 0 || parameterDef.typeIndex >= il2Cpp.types.Length)
                                continue;
                            var parameterName = metadata.GetStringFromIndex(parameterDef.nameIndex);
                            var parameterType = il2Cpp.types[parameterDef.typeIndex];
                            var parameterTypeRef = GetTypeReferenceWithByRef(methodDefinition, parameterType);
                            var parameterDefinition = new ParameterDefinition(parameterName, (ParameterAttributes)parameterType.attrs, parameterTypeRef);
                            methodDefinition.Parameters.Add(parameterDefinition);
                            parameterDefinitionDic[parameterIndex] = parameterDefinition;
                            //ParameterDefault
                            if (metadata.GetParameterDefaultValueFromIndex(parameterIndex, out var parameterDefault) && parameterDefault.dataIndex != -1)
                            {
                                if (executor.TryGetDefaultValue(parameterDefault.typeIndex, parameterDefault.dataIndex, out var value) && CanWriteDefaultConstant(parameterTypeRef, value))
                                {
                                    parameterDefinition.Constant = value;
                                }
                                else
                                {
                                    var customAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(metadataOffsetAttribute));
                                    var offset = new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{value:X}"));
                                    customAttribute.Fields.Add(offset);
                                    parameterDefinition.CustomAttributes.Add(customAttribute);
                                }
                            }
                        }
                        //methodAddress
                        if (!methodDefinition.IsAbstract)
                        {
                            var methodPointer = il2Cpp.GetMethodPointer(imageName, methodDef);
                            if (methodPointer > 0)
                            {
                                var customAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(addressAttribute));
                                var fixedMethodPointer = il2Cpp.GetRVA(methodPointer);
                                var rva = new CustomAttributeNamedArgument("RVA", new CustomAttributeArgument(stringType, $"0x{fixedMethodPointer:X}"));
                                var offset = new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{il2Cpp.MapVATR(methodPointer):X}"));
                                var va = new CustomAttributeNamedArgument("VA", new CustomAttributeArgument(stringType, $"0x{methodPointer:X}"));
                                customAttribute.Fields.Add(rva);
                                customAttribute.Fields.Add(offset);
                                customAttribute.Fields.Add(va);
                                if (methodDef.slot != ushort.MaxValue)
                                {
                                    var slot = new CustomAttributeNamedArgument("Slot", new CustomAttributeArgument(stringType, methodDef.slot.ToString()));
                                    customAttribute.Fields.Add(slot);
                                }
                                methodDefinition.CustomAttributes.Add(customAttribute);
                            }
                        }
                    }
                    //property
                    var propertyEnd = typeDef.propertyStart >= 0 ? Math.Min(metadata.propertyDefs.Length, typeDef.propertyStart + typeDef.property_count) : 0;
                    for (var i = Math.Max(0, typeDef.propertyStart); i < propertyEnd; ++i)
                    {
                        var propertyDef = metadata.propertyDefs[i];
                        var propertyName = metadata.GetStringFromIndex(propertyDef.nameIndex);
                        TypeReference propertyType = null;
                        MethodDefinition GetMethod = null;
                        MethodDefinition SetMethod = null;
                        if (propertyDef.get >= 0 && methodDefinitionDic.TryGetValue(typeDef.methodStart + propertyDef.get, out var getMethod))
                        {
                            GetMethod = getMethod;
                            propertyType = GetMethod.ReturnType;
                        }
                        if (propertyDef.set >= 0 && methodDefinitionDic.TryGetValue(typeDef.methodStart + propertyDef.set, out var setMethod))
                        {
                            SetMethod = setMethod;
                            if (SetMethod.Parameters.Count > 0)
                                propertyType ??= SetMethod.Parameters[0].ParameterType;
                        }
                        if (propertyType == null)
                            continue;
                        var propertyDefinition = new PropertyDefinition(propertyName, (PropertyAttributes)propertyDef.attrs, propertyType)
                        {
                            GetMethod = GetMethod,
                            SetMethod = SetMethod
                        };
                        typeDefinition.Properties.Add(propertyDefinition);
                        propertyDefinitionDic[i] = propertyDefinition;

                        if (addToken)
                        {
                            var customTokenAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(tokenAttribute));
                            customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{propertyDef.token:X}")));
                            propertyDefinition.CustomAttributes.Add(customTokenAttribute);
                        }
                    }
                    //event
                    var eventEnd = typeDef.eventStart >= 0 ? Math.Min(metadata.eventDefs.Length, typeDef.eventStart + typeDef.event_count) : 0;
                    for (var i = Math.Max(0, typeDef.eventStart); i < eventEnd; ++i)
                    {
                        var eventDef = metadata.eventDefs[i];
                        if (eventDef.typeIndex < 0 || eventDef.typeIndex >= il2Cpp.types.Length)
                            continue;
                        var eventName = metadata.GetStringFromIndex(eventDef.nameIndex);
                        var eventType = il2Cpp.types[eventDef.typeIndex];
                        var eventTypeRef = GetTypeReference(typeDefinition, eventType);
                        var eventDefinition = new EventDefinition(eventName, (EventAttributes)eventType.attrs, eventTypeRef);
                        if (eventDef.add >= 0 && methodDefinitionDic.TryGetValue(typeDef.methodStart + eventDef.add, out var addMethod))
                            eventDefinition.AddMethod = addMethod;
                        if (eventDef.remove >= 0 && methodDefinitionDic.TryGetValue(typeDef.methodStart + eventDef.remove, out var removeMethod))
                            eventDefinition.RemoveMethod = removeMethod;
                        if (eventDef.raise >= 0 && methodDefinitionDic.TryGetValue(typeDef.methodStart + eventDef.raise, out var raiseMethod))
                            eventDefinition.InvokeMethod = raiseMethod;
                        typeDefinition.Events.Add(eventDefinition);
                        eventDefinitionDic[i] = eventDefinition;

                        if (addToken)
                        {
                            var customTokenAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(tokenAttribute));
                            customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{eventDef.token:X}")));
                            eventDefinition.CustomAttributes.Add(customTokenAttribute);
                        }
                    }
                }
            }
            //第三遍，添加CustomAttribute
            if (il2Cpp.Version > 20)
            {
                foreach (var imageDef in metadata.imageDefs)
                {
                    var typeEnd = imageDef.typeStart + imageDef.typeCount;
                    for (int index = imageDef.typeStart; index < typeEnd; index++)
                    {
                        var typeDef = metadata.typeDefs[index];
                        var typeDefinition = typeDefinitionDic[typeDef];
                        //typeAttribute
                        CreateCustomAttribute(imageDef, typeDef.customAttributeIndex, typeDef.token, typeDefinition.Module, typeDefinition.CustomAttributes);

                        //field
                        var fieldEnd = typeDef.fieldStart >= 0 ? Math.Min(metadata.fieldDefs.Length, typeDef.fieldStart + typeDef.field_count) : 0;
                        for (var i = Math.Max(0, typeDef.fieldStart); i < fieldEnd; ++i)
                        {
                            var fieldDef = metadata.fieldDefs[i];
                            if (!fieldDefinitionDic.TryGetValue(i, out var fieldDefinition))
                                continue;
                            //fieldAttribute
                            CreateCustomAttribute(imageDef, fieldDef.customAttributeIndex, fieldDef.token, typeDefinition.Module, fieldDefinition.CustomAttributes);
                        }

                        //method
                        var methodEnd = typeDef.methodStart >= 0 ? Math.Min(metadata.methodDefs.Length, typeDef.methodStart + typeDef.method_count) : 0;
                        for (var i = Math.Max(0, typeDef.methodStart); i < methodEnd; ++i)
                        {
                            var methodDef = metadata.methodDefs[i];
                            if (!methodDefinitionDic.TryGetValue(i, out var methodDefinition))
                                continue;
                            //methodAttribute
                            CreateCustomAttribute(imageDef, methodDef.customAttributeIndex, methodDef.token, typeDefinition.Module, methodDefinition.CustomAttributes);

                            //method parameter
                            for (var j = 0; j < methodDef.parameterCount; ++j)
                            {
                                var parameterIndex = methodDef.parameterStart + j;
                                if (parameterIndex < 0 || parameterIndex >= metadata.parameterDefs.Length || !parameterDefinitionDic.TryGetValue(parameterIndex, out var parameterDefinition))
                                    continue;
                                var parameterDef = metadata.parameterDefs[parameterIndex];
                                //parameterAttribute
                                CreateCustomAttribute(imageDef, parameterDef.customAttributeIndex, parameterDef.token, typeDefinition.Module, parameterDefinition.CustomAttributes);
                            }
                        }

                        //property
                        var propertyEnd = typeDef.propertyStart >= 0 ? Math.Min(metadata.propertyDefs.Length, typeDef.propertyStart + typeDef.property_count) : 0;
                        for (var i = Math.Max(0, typeDef.propertyStart); i < propertyEnd; ++i)
                        {
                            var propertyDef = metadata.propertyDefs[i];
                            if (!propertyDefinitionDic.TryGetValue(i, out var propertyDefinition))
                                continue;
                            //propertyAttribute
                            CreateCustomAttribute(imageDef, propertyDef.customAttributeIndex, propertyDef.token, typeDefinition.Module, propertyDefinition.CustomAttributes);
                        }

                        //event
                        var eventEnd = typeDef.eventStart >= 0 ? Math.Min(metadata.eventDefs.Length, typeDef.eventStart + typeDef.event_count) : 0;
                        for (var i = Math.Max(0, typeDef.eventStart); i < eventEnd; ++i)
                        {
                            var eventDef = metadata.eventDefs[i];
                            if (!eventDefinitionDic.TryGetValue(i, out var eventDefinition))
                                continue;
                            //eventAttribute
                            CreateCustomAttribute(imageDef, eventDef.customAttributeIndex, eventDef.token, typeDefinition.Module, eventDefinition.CustomAttributes);
                        }
                    }
                }
            }
        }

        private TypeReference GetTypeReferenceWithByRef(MemberReference memberReference, Il2CppType il2CppType)
        {
            if (il2CppType == null)
                return memberReference.Module.ImportReference(typeSystem.Object);
            var typeReference = GetTypeReference(memberReference, il2CppType);
            if (il2CppType.byref == 1)
            {
                return new ByReferenceType(typeReference);
            }
            else
            {
                return typeReference;
            }
        }

        private static bool CanWriteDefaultConstant(TypeReference typeReference, object value)
        {
            if (value == null)
            {
                return true;
            }
            if (typeReference == null || typeReference.IsValueType && typeReference.MetadataType == MetadataType.ValueType)
            {
                return false;
            }
            return typeReference.MetadataType switch
            {
                MetadataType.Boolean or MetadataType.Char or MetadataType.SByte or MetadataType.Byte
                    or MetadataType.Int16 or MetadataType.UInt16 or MetadataType.Int32 or MetadataType.UInt32
                    or MetadataType.Int64 or MetadataType.UInt64 or MetadataType.Single or MetadataType.Double
                    or MetadataType.String => true,
                _ => false
            };
        }

        private TypeReference GetTypeReference(MemberReference memberReference, Il2CppType il2CppType)
        {
            var moduleDefinition = memberReference.Module;
            if (il2CppType == null)
                return moduleDefinition.ImportReference(typeSystem.Object);
            switch (il2CppType.type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_OBJECT:
                    return moduleDefinition.ImportReference(typeSystem.Object);
                case Il2CppTypeEnum.IL2CPP_TYPE_VOID:
                    return moduleDefinition.ImportReference(typeSystem.Void);
                case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                    return moduleDefinition.ImportReference(typeSystem.Boolean);
                case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                    return moduleDefinition.ImportReference(typeSystem.Char);
                case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                    return moduleDefinition.ImportReference(typeSystem.SByte);
                case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                    return moduleDefinition.ImportReference(typeSystem.Byte);
                case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                    return moduleDefinition.ImportReference(typeSystem.Int16);
                case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                    return moduleDefinition.ImportReference(typeSystem.UInt16);
                case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                    return moduleDefinition.ImportReference(typeSystem.Int32);
                case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                    return moduleDefinition.ImportReference(typeSystem.UInt32);
                case Il2CppTypeEnum.IL2CPP_TYPE_I:
                    return moduleDefinition.ImportReference(typeSystem.IntPtr);
                case Il2CppTypeEnum.IL2CPP_TYPE_U:
                    return moduleDefinition.ImportReference(typeSystem.UIntPtr);
                case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                    return moduleDefinition.ImportReference(typeSystem.Int64);
                case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                    return moduleDefinition.ImportReference(typeSystem.UInt64);
                case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                    return moduleDefinition.ImportReference(typeSystem.Single);
                case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                    return moduleDefinition.ImportReference(typeSystem.Double);
                case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                    return moduleDefinition.ImportReference(typeSystem.String);
                case Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF:
                    return moduleDefinition.ImportReference(typeSystem.TypedReference);
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                    {
                        var typeDef = executor.GetTypeDefinitionFromIl2CppType(il2CppType);
                        if (typeDef == null || !typeDefinitionDic.TryGetValue(typeDef, out var typeDefinition))
                            return moduleDefinition.ImportReference(typeSystem.Object);
                        return moduleDefinition.ImportReference(typeDefinition);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                    {
                        var arrayType = il2Cpp.MapVATR<Il2CppArrayType>(il2CppType.data.array);
                        var oriType = il2Cpp.GetIl2CppType(arrayType.etype);
                        return new ArrayType(GetTypeReference(memberReference, oriType), arrayType.rank);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                    {
                        var genericClass = il2Cpp.MapVATR<Il2CppGenericClass>(il2CppType.data.generic_class);
                        var typeDef = executor.GetGenericClassTypeDefinition(genericClass);
                        if (typeDef == null || !typeDefinitionDic.TryGetValue(typeDef, out var typeDefinition))
                            return moduleDefinition.ImportReference(typeSystem.Object);
                        var genericInstanceType = new GenericInstanceType(moduleDefinition.ImportReference(typeDefinition));
                        if (TryGetGenericInstPointers(genericClass.context.class_inst, out var pointers))
                        {
                            foreach (var pointer in pointers)
                            {
                                var oriType = il2Cpp.GetIl2CppType(pointer);
                                if (oriType != null)
                                {
                                    genericInstanceType.GenericArguments.Add(GetTypeReference(memberReference, oriType));
                                }
                            }
                        }
                        return genericInstanceType;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                    {
                        var oriType = il2Cpp.GetIl2CppType(il2CppType.data.type);
                        return new ArrayType(GetTypeReference(memberReference, oriType));
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                    {
                        if (memberReference is MethodDefinition methodDefinition)
                        {
                            var param = executor.GetGenericParameteFromIl2CppType(il2CppType);
                            return ResolveExistingGenericParameter(param, methodDefinition.DeclaringType) ?? moduleDefinition.ImportReference(typeSystem.Object);
                        }
                        var typeDefinition = (TypeDefinition)memberReference;
                        var typeParam = executor.GetGenericParameteFromIl2CppType(il2CppType);
                        return ResolveExistingGenericParameter(typeParam, typeDefinition) ?? moduleDefinition.ImportReference(typeSystem.Object);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                    {
                        var methodDefinition = (MethodDefinition)memberReference;
                        var param = executor.GetGenericParameteFromIl2CppType(il2CppType);
                        return ResolveExistingGenericParameter(param, methodDefinition) ?? moduleDefinition.ImportReference(typeSystem.Object);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                    {
                        var oriType = il2Cpp.GetIl2CppType(il2CppType.data.type);
                        return new PointerType(GetTypeReference(memberReference, oriType));
                    }
                default:
                    return moduleDefinition.ImportReference(typeSystem.Object);
            }
        }

        private bool TryGetGenericInstPointers(ulong genericInstPointer, out ulong[] pointers)
        {
            pointers = null;
            if (genericInstPointer == 0)
            {
                return false;
            }

            if (!TryMapVATR(genericInstPointer, out Il2CppGenericInst genericInst) || genericInst.type_argc < 0)
            {
                return false;
            }
            return TryMapVATR(genericInst.type_argv, genericInst.type_argc, out pointers);
        }

        private bool TryMapVATR<T>(ulong pointer, out T value) where T : new()
        {
            value = default;
            if (pointer == 0)
            {
                return false;
            }
            try
            {
                value = il2Cpp.MapVATR<T>(pointer);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }

        private bool TryMapVATR<T>(ulong pointer, long count, out T[] values) where T : new()
        {
            values = null;
            if (pointer == 0 || count < 0)
            {
                return false;
            }
            try
            {
                values = il2Cpp.MapVATR<T>(pointer, count);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }

        private void CreateCustomAttribute(Il2CppImageDefinition imageDef, int customAttributeIndex, uint token, ModuleDefinition moduleDefinition, Collection<CustomAttribute> customAttributes)
        {
            var attributeIndex = metadata.GetCustomAttributeIndex(imageDef, customAttributeIndex, token);
            if (attributeIndex >= 0)
            {
                try
                {
                    if (il2Cpp.Version < 29)
                    {
                        var attributeTypeRange = metadata.attributeTypeRanges[attributeIndex];
                        for (int i = 0; i < attributeTypeRange.count; i++)
                        {
                            var attributeTypeIndex = metadata.attributeTypes[attributeTypeRange.start + i];
                            var attributeType = il2Cpp.types[attributeTypeIndex];
                            var typeDef = executor.GetTypeDefinitionFromIl2CppType(attributeType);
                            var typeDefinition = typeDefinitionDic[typeDef];
                            if (!TryRestoreCustomAttribute(typeDefinition, moduleDefinition, customAttributes))
                            {
                                var methodPointer = executor.customAttributeGenerators[attributeIndex];
                                var fixedMethodPointer = il2Cpp.GetRVA(methodPointer);
                                var customAttribute = new CustomAttribute(moduleDefinition.ImportReference(attributeAttribute));
                                var name = new CustomAttributeNamedArgument("Name", new CustomAttributeArgument(stringType, typeDefinition.Name));
                                var rva = new CustomAttributeNamedArgument("RVA", new CustomAttributeArgument(stringType, $"0x{fixedMethodPointer:X}"));
                                var offset = new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{il2Cpp.MapVATR(methodPointer):X}"));
                                customAttribute.Fields.Add(name);
                                customAttribute.Fields.Add(rva);
                                customAttribute.Fields.Add(offset);
                                customAttributes.Add(customAttribute);
                            }
                        }
                    }
                    else
                    {
                        var startRange = metadata.attributeDataRanges[attributeIndex];
                        var endRange = metadata.attributeDataRanges[attributeIndex + 1];
                        metadata.Position = metadata.header.attributeDataOffset + startRange.startOffset;
                        var buff = metadata.ReadBytes((int)(endRange.startOffset - startRange.startOffset));
                        var reader = new CustomAttributeDataReader(executor, buff);
                        if (reader.Count != 0)
                        {
                            for (var i = 0; i < reader.Count; i++)
                            {
                                var visitor = reader.VisitCustomAttributeData();
                                var methodDefinition = methodDefinitionDic[visitor.CtorIndex];
                                var customAttribute = new CustomAttribute(moduleDefinition.ImportReference(methodDefinition));
                                foreach (var argument in visitor.Arguments)
                                {
                                    var parameterDefinition = methodDefinition.Parameters[argument.Index];
                                    var customAttributeArgument = CreateCustomAttributeArgument(parameterDefinition.ParameterType, argument.Value, methodDefinition);
                                    customAttribute.ConstructorArguments.Add(customAttributeArgument);
                                }
                                foreach (var field in visitor.Fields)
                                {
                                    var fieldDefinition = fieldDefinitionDic[field.Index];
                                    var customAttributeArgument = CreateCustomAttributeArgument(fieldDefinition.FieldType, field.Value, fieldDefinition);
                                    var customAttributeNamedArgument = new CustomAttributeNamedArgument(fieldDefinition.Name, customAttributeArgument);
                                    customAttribute.Fields.Add(customAttributeNamedArgument);
                                }
                                foreach (var property in visitor.Properties)
                                {
                                    var propertyDefinition = propertyDefinitionDic[property.Index];
                                    var customAttributeArgument = CreateCustomAttributeArgument(propertyDefinition.PropertyType, property.Value, propertyDefinition);
                                    var customAttributeNamedArgument = new CustomAttributeNamedArgument(propertyDefinition.Name, customAttributeArgument);
                                    customAttribute.Properties.Add(customAttributeNamedArgument);
                                }
                                customAttributes.Add(customAttribute);
                            }
                        }
                    }
                }
                catch
                {
                    Console.WriteLine($"ERROR: Error while restoring attributeIndex {attributeIndex}");
                }
            }
        }

        private static bool TryRestoreCustomAttribute(TypeDefinition attributeType, ModuleDefinition moduleDefinition, Collection<CustomAttribute> customAttributes)
        {
            if (attributeType.Methods.Count == 1 && attributeType.Name != "CompilerGeneratedAttribute")
            {
                var methodDefinition = attributeType.Methods[0];
                if (methodDefinition.Name == ".ctor" && methodDefinition.Parameters.Count == 0)
                {
                    var customAttribute = new CustomAttribute(moduleDefinition.ImportReference(methodDefinition));
                    customAttributes.Add(customAttribute);
                    return true;
                }
            }
            return false;
        }

        private GenericParameter CreateGenericParameter(Il2CppGenericParameter param, IGenericParameterProvider iGenericParameterProvider)
        {
            if (param == null)
            {
                return new GenericParameter("T", iGenericParameterProvider);
            }
            var key = (param, iGenericParameterProvider);
            if (!genericParameterDic.TryGetValue(key, out var genericParameter))
            {
                var genericName = metadata.GetStringFromIndex(param.nameIndex);
                genericParameter = new GenericParameter(genericName, iGenericParameterProvider)
                {
                    Attributes = (GenericParameterAttributes)param.flags
                };
                genericParameterDic.Add(key, genericParameter);
                for (int i = 0; i < param.constraintsCount; ++i)
                {
                    var il2CppType = il2Cpp.types[metadata.constraintIndices[param.constraintsStart + i]];
                    genericParameter.Constraints.Add(new GenericParameterConstraint(GetTypeReference((MemberReference)iGenericParameterProvider, il2CppType)));
                }
            }
            return genericParameter;
        }

        private GenericParameter ResolveExistingGenericParameter(Il2CppGenericParameter param, IGenericParameterProvider provider)
        {
            if (param == null || provider == null || !provider.HasGenericParameters)
            {
                return null;
            }
            if (param.num < provider.GenericParameters.Count)
            {
                return provider.GenericParameters[param.num];
            }
            var genericName = metadata.GetStringFromIndex(param.nameIndex);
            return provider.GenericParameters.FirstOrDefault(x => x.Name == genericName);
        }

        private CustomAttributeArgument CreateCustomAttributeArgument(TypeReference typeReference, BlobValue blobValue, MemberReference memberReference)
        {
            var val = blobValue.Value;
            if (typeReference.FullName == "System.Object")
            {
                if (blobValue.il2CppTypeEnum == Il2CppTypeEnum.IL2CPP_TYPE_IL2CPP_TYPE_INDEX)
                {
                    val = new CustomAttributeArgument(memberReference.Module.ImportReference(typeof(Type)), GetTypeReference(memberReference, (Il2CppType)val));
                }
                else
                {
                    val = new CustomAttributeArgument(GetBlobValueTypeReference(blobValue, memberReference), val);
                }
            }
            else if (val == null)
            {
                return new CustomAttributeArgument(typeReference, val);
            }
            else if (typeReference is ArrayType arrayType)
            {
                var arrayVal = (BlobValue[])val;
                var array = new CustomAttributeArgument[arrayVal.Length];
                var elementType = arrayType.ElementType;
                for (int i = 0; i < arrayVal.Length; i++)
                {
                    array[i] = CreateCustomAttributeArgument(elementType, arrayVal[i], memberReference);
                }
                val = array;
            }
            else if (typeReference.FullName == "System.Type")
            {
                val = GetTypeReference(memberReference, (Il2CppType)val);
            }
            return new CustomAttributeArgument(typeReference, val);
        }

        private TypeReference GetBlobValueTypeReference(BlobValue blobValue, MemberReference memberReference)
        {
            if (blobValue.EnumType != null)
            {
                return GetTypeReference(memberReference, blobValue.EnumType);
            }
            var il2CppType = new Il2CppType
            {
                type = blobValue.il2CppTypeEnum
            };
            return GetTypeReference(memberReference, il2CppType);
        }
    }
}
