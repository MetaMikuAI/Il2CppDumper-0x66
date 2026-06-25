using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Il2CppDumper
{
    public sealed class Metadata : BinaryStream
    {
        public Il2CppGlobalMetadataHeader header;
        public Il2CppImageDefinition[] imageDefs;
        public Il2CppAssemblyDefinition[] assemblyDefs;
        public Il2CppTypeDefinition[] typeDefs;
        public Il2CppMethodDefinition[] methodDefs;
        public Il2CppParameterDefinition[] parameterDefs;
        public Il2CppFieldDefinition[] fieldDefs;
        private Dictionary<int, Il2CppFieldDefaultValue> fieldDefaultValuesDic;
        private Dictionary<int, Il2CppParameterDefaultValue> parameterDefaultValuesDic;
        public Il2CppPropertyDefinition[] propertyDefs;
        public Il2CppCustomAttributeTypeRange[] attributeTypeRanges;
        public Il2CppCustomAttributeDataRange[] attributeDataRanges;
        private readonly Dictionary<Il2CppImageDefinition, Dictionary<uint, int>> attributeTypeRangesDic;
        public Il2CppStringLiteral[] stringLiterals;
        private readonly Il2CppMetadataUsageList[] metadataUsageLists;
        private readonly Il2CppMetadataUsagePair[] metadataUsagePairs;
        public int[] attributeTypes;
        public int[] interfaceIndices;
        public Dictionary<Il2CppMetadataUsage, SortedDictionary<uint, uint>> metadataUsageDic;
        public long metadataUsagesCount;
        public int[] nestedTypeIndices;
        public Il2CppEventDefinition[] eventDefs;
        public Il2CppGenericContainer[] genericContainers;
        public Il2CppFieldRef[] fieldRefs;
        public Il2CppGenericParameter[] genericParameters;
        public int[] constraintIndices;
        public uint[] vtableMethods;
        public Il2CppRGCTXDefinition[] rgctxEntries;

        private readonly Dictionary<uint, string> stringCache = new();
        public bool IsAceMetadataLayout { get; }

        public Metadata(Stream stream, bool useAceMetadataLayout = false) : base(stream)
        {
            var sanity = ReadUInt32();
            var version = ReadInt32();
            if (useAceMetadataLayout)
            {
                IsAceMetadataLayout = true;
                InitAceMetadata(sanity, version);
                return;
            }
            if (sanity != 0xFAB11BAF)
            {
                throw new InvalidDataException("ERROR: Metadata file supplied is not valid metadata file.");
            }
            if (version < 0 || version > 1000)
            {
                throw new InvalidDataException("ERROR: Metadata file supplied is not valid metadata file.");
            }
            if (version < 16 || version > 31)
            {
                throw new NotSupportedException($"ERROR: Metadata file supplied is not a supported version[{version}].");
            }
            Version = version;
            header = ReadClass<Il2CppGlobalMetadataHeader>(0);
            if (version == 24)
            {
                if (header.stringLiteralOffset == 264)
                {
                    Version = 24.2;
                    header = ReadClass<Il2CppGlobalMetadataHeader>(0);
                }
                else
                {
                    imageDefs = ReadMetadataClassArray<Il2CppImageDefinition>(header.imagesOffset, header.imagesSize);
                    if (imageDefs.Any(x => x.token != 1))
                    {
                        Version = 24.1;
                    }
                }
            }
            imageDefs = ReadMetadataClassArray<Il2CppImageDefinition>(header.imagesOffset, header.imagesSize);
            if (Version == 24.2 && header.assembliesSize / 68 < imageDefs.Length)
            {
                Version = 24.4;
            }
            var v241Plus = false;
            if (Version == 24.1 && header.assembliesSize / 64 == imageDefs.Length)
            {
                v241Plus = true;
            }
            if (v241Plus)
            {
                Version = 24.4;
            }
            assemblyDefs = ReadMetadataClassArray<Il2CppAssemblyDefinition>(header.assembliesOffset, header.assembliesSize);
            if (v241Plus)
            {
                Version = 24.1;
            }
            typeDefs = ReadMetadataClassArray<Il2CppTypeDefinition>(header.typeDefinitionsOffset, header.typeDefinitionsSize);
            methodDefs = ReadMetadataClassArray<Il2CppMethodDefinition>(header.methodsOffset, header.methodsSize);
            parameterDefs = ReadMetadataClassArray<Il2CppParameterDefinition>(header.parametersOffset, header.parametersSize);
            fieldDefs = ReadMetadataClassArray<Il2CppFieldDefinition>(header.fieldsOffset, header.fieldsSize);
            var fieldDefaultValues = ReadMetadataClassArray<Il2CppFieldDefaultValue>(header.fieldDefaultValuesOffset, header.fieldDefaultValuesSize);
            var parameterDefaultValues = ReadMetadataClassArray<Il2CppParameterDefaultValue>(header.parameterDefaultValuesOffset, header.parameterDefaultValuesSize);
            fieldDefaultValuesDic = fieldDefaultValues.ToDictionary(x => x.fieldIndex);
            parameterDefaultValuesDic = parameterDefaultValues.ToDictionary(x => x.parameterIndex);
            propertyDefs = ReadMetadataClassArray<Il2CppPropertyDefinition>(header.propertiesOffset, header.propertiesSize);
            interfaceIndices = ReadClassArray<int>(header.interfacesOffset, header.interfacesSize / 4);
            nestedTypeIndices = ReadClassArray<int>(header.nestedTypesOffset, header.nestedTypesSize / 4);
            eventDefs = ReadMetadataClassArray<Il2CppEventDefinition>(header.eventsOffset, header.eventsSize);
            genericContainers = ReadMetadataClassArray<Il2CppGenericContainer>(header.genericContainersOffset, header.genericContainersSize);
            genericParameters = ReadMetadataClassArray<Il2CppGenericParameter>(header.genericParametersOffset, header.genericParametersSize);
            constraintIndices = ReadClassArray<int>(header.genericParameterConstraintsOffset, header.genericParameterConstraintsSize / 4);
            vtableMethods = ReadClassArray<uint>(header.vtableMethodsOffset, header.vtableMethodsSize / 4);
            stringLiterals = ReadMetadataClassArray<Il2CppStringLiteral>(header.stringLiteralOffset, header.stringLiteralSize);
            if (Version > 16)
            {
                fieldRefs = ReadMetadataClassArray<Il2CppFieldRef>(header.fieldRefsOffset, header.fieldRefsSize);
                if (Version < 27)
                {
                    metadataUsageLists = ReadMetadataClassArray<Il2CppMetadataUsageList>(header.metadataUsageListsOffset, header.metadataUsageListsCount);
                    metadataUsagePairs = ReadMetadataClassArray<Il2CppMetadataUsagePair>(header.metadataUsagePairsOffset, header.metadataUsagePairsCount);

                    ProcessingMetadataUsage();
                }
            }
            if (Version > 20 && Version < 29)
            {
                attributeTypeRanges = ReadMetadataClassArray<Il2CppCustomAttributeTypeRange>(header.attributesInfoOffset, header.attributesInfoCount);
                attributeTypes = ReadClassArray<int>(header.attributeTypesOffset, header.attributeTypesCount / 4);
            }
            if (Version >= 29)
            {
                attributeDataRanges = ReadMetadataClassArray<Il2CppCustomAttributeDataRange>(header.attributeDataRangeOffset, header.attributeDataRangeSize);
            }
            if (Version > 24)
            {
                attributeTypeRangesDic = new Dictionary<Il2CppImageDefinition, Dictionary<uint, int>>();
                foreach (var imageDef in imageDefs)
                {
                    var dic = new Dictionary<uint, int>();
                    attributeTypeRangesDic[imageDef] = dic;
                    var end = imageDef.customAttributeStart + imageDef.customAttributeCount;
                    for (int i = imageDef.customAttributeStart; i < end; i++)
                    {
                        if (Version >= 29)
                        {
                            dic.Add(attributeDataRanges[i].token, i);
                        }
                        else
                        {
                            dic.Add(attributeTypeRanges[i].token, i);
                        }
                    }
                }
            }
            if (Version <= 24.1)
            {
                rgctxEntries = ReadMetadataClassArray<Il2CppRGCTXDefinition>(header.rgctxEntriesOffset, header.rgctxEntriesCount);
            }
        }

        private void InitAceMetadata(uint firstDword, int version)
        {
            if (version < 16 || version > 31)
            {
                throw new NotSupportedException($"ERROR: ACE metadata version[{version}] is not supported.");
            }
            Version = version;
            var sections = ReadAceSections(firstDword);
            header = BuildAceHeader(sections);
            imageDefs = ReadAceImageDefinitions(header.imagesOffset, sections[20].Count);
            assemblyDefs = ReadAceAssemblyDefinitions(header.assembliesOffset, sections[21].Count);
            typeDefs = ReadAceTypeDefinitions(header.typeDefinitionsOffset, sections[19].Count);
            methodDefs = ReadAceMethodDefinitions(header.methodsOffset, sections[5].Count);
            parameterDefs = ReadAceParameterDefinitions(header.parametersOffset, sections[10].Count);
            fieldDefs = ReadAceFieldDefinitions(header.fieldsOffset, sections[11].Count);
            var fieldDefaultValues = ReadMetadataClassArray<Il2CppFieldDefaultValue>(header.fieldDefaultValuesOffset, header.fieldDefaultValuesSize);
            var parameterDefaultValues = ReadMetadataClassArray<Il2CppParameterDefaultValue>(header.parameterDefaultValuesOffset, header.parameterDefaultValuesSize);
            fieldDefaultValuesDic = fieldDefaultValues.GroupBy(x => x.fieldIndex).ToDictionary(x => x.Key, x => x.First());
            parameterDefaultValuesDic = parameterDefaultValues.GroupBy(x => x.parameterIndex).ToDictionary(x => x.Key, x => x.First());
            propertyDefs = ReadMetadataClassArray<Il2CppPropertyDefinition>(header.propertiesOffset, header.propertiesSize);
            interfaceIndices = ReadClassArray<int>(header.interfacesOffset, header.interfacesSize / 4);
            nestedTypeIndices = ReadClassArray<int>(header.nestedTypesOffset, header.nestedTypesSize / 4);
            eventDefs = ReadMetadataClassArray<Il2CppEventDefinition>(header.eventsOffset, header.eventsSize);
            genericContainers = ReadMetadataClassArray<Il2CppGenericContainer>(header.genericContainersOffset, header.genericContainersSize);
            RestoreAceGenericContainerIndices();
            genericParameters = ReadAceGenericParameters(header.genericParametersOffset, sections[12].Count);
            constraintIndices = ReadClassArray<int>(header.genericParameterConstraintsOffset, header.genericParameterConstraintsSize / 4);
            vtableMethods = ReadClassArray<uint>(header.vtableMethodsOffset, header.vtableMethodsSize / 4);
            stringLiterals = ReadAceStringLiterals(header.stringLiteralOffset, sections[0].Count, header.stringLiteralDataSize);
            fieldRefs = ReadMetadataClassArray<Il2CppFieldRef>(header.fieldRefsOffset, header.fieldRefsSize);
            attributeTypeRanges = Array.Empty<Il2CppCustomAttributeTypeRange>();
            attributeTypes = Array.Empty<int>();
            attributeDataRanges = Array.Empty<Il2CppCustomAttributeDataRange>();
            metadataUsagesCount = 0;
            rgctxEntries = Array.Empty<Il2CppRGCTXDefinition>();
        }

        private void RestoreAceGenericContainerIndices()
        {
            for (var i = 0; i < genericContainers.Length; i++)
            {
                var genericContainer = genericContainers[i];
                if (genericContainer.is_method != 0)
                {
                    if (genericContainer.ownerIndex >= 0 && genericContainer.ownerIndex < methodDefs.Length)
                    {
                        methodDefs[genericContainer.ownerIndex].genericContainerIndex = i;
                    }
                }
                else if (genericContainer.ownerIndex >= 0 && genericContainer.ownerIndex < typeDefs.Length)
                {
                    typeDefs[genericContainer.ownerIndex].genericContainerIndex = i;
                }
            }
        }

        private static int ToSize(uint size) => checked((int)size);

        private AceSection[] ReadAceSections(uint firstDword)
        {
            if (firstDword != 0)
            {
                throw new InvalidDataException("ERROR: ACE metadata header marker is not recognized.");
            }
            var sections = new AceSection[31];
            for (var i = 0; i < sections.Length; i++)
            {
                var baseOffset = 8ul + (ulong)i * 12ul;
                Position = baseOffset;
                sections[i] = new AceSection(ReadUInt32(), ReadUInt32(), ReadUInt32());
            }
            return sections;
        }

        private static Il2CppGlobalMetadataHeader BuildAceHeader(AceSection[] s)
        {
            return new Il2CppGlobalMetadataHeader
            {
                sanity = 0xFAB11BAF,
                version = 24,
                stringLiteralOffset = s[0].Offset,
                stringLiteralSize = ToSize(s[0].Size),
                stringLiteralDataOffset = s[1].Offset,
                stringLiteralDataSize = ToSize(s[1].Size),
                stringOffset = s[2].Offset,
                stringSize = ToSize(s[2].Size),
                eventsOffset = s[3].Offset,
                eventsSize = ToSize(s[3].Size),
                propertiesOffset = s[4].Offset,
                propertiesSize = ToSize(s[4].Size),
                methodsOffset = s[5].Offset,
                methodsSize = ToSize(s[5].Size),
                parameterDefaultValuesOffset = s[6].Offset,
                parameterDefaultValuesSize = ToSize(s[6].Size),
                fieldDefaultValuesOffset = s[7].Offset,
                fieldDefaultValuesSize = ToSize(s[7].Size),
                fieldAndParameterDefaultValueDataOffset = s[8].Offset,
                fieldAndParameterDefaultValueDataSize = ToSize(s[8].Size),
                fieldMarshaledSizesOffset = (int)s[9].Offset,
                fieldMarshaledSizesSize = ToSize(s[9].Size),
                parametersOffset = s[10].Offset,
                parametersSize = ToSize(s[10].Size),
                fieldsOffset = s[11].Offset,
                fieldsSize = ToSize(s[11].Size),
                genericParametersOffset = s[12].Offset,
                genericParametersSize = ToSize(s[12].Size),
                genericParameterConstraintsOffset = s[13].Offset,
                genericParameterConstraintsSize = ToSize(s[13].Size),
                genericContainersOffset = s[14].Offset,
                genericContainersSize = ToSize(s[14].Size),
                nestedTypesOffset = s[15].Offset,
                nestedTypesSize = ToSize(s[15].Size),
                interfacesOffset = s[16].Offset,
                interfacesSize = ToSize(s[16].Size),
                vtableMethodsOffset = s[17].Offset,
                vtableMethodsSize = ToSize(s[17].Size),
                interfaceOffsetsOffset = (int)s[18].Offset,
                interfaceOffsetsSize = ToSize(s[18].Size),
                typeDefinitionsOffset = s[19].Offset,
                typeDefinitionsSize = ToSize(s[19].Size),
                imagesOffset = s[20].Offset,
                imagesSize = ToSize(s[20].Size),
                assembliesOffset = s[21].Offset,
                assembliesSize = ToSize(s[21].Size),
                fieldRefsOffset = s[22].Offset,
                fieldRefsSize = ToSize(s[22].Size),
                referencedAssembliesOffset = (int)s[23].Offset,
                referencedAssembliesSize = ToSize(s[23].Size),
                attributeDataOffset = s[24].Offset,
                attributeDataSize = ToSize(s[24].Size),
                attributeDataRangeOffset = s[25].Offset,
                attributeDataRangeSize = ToSize(s[25].Size),
                unresolvedVirtualCallParameterTypesOffset = (int)s[26].Offset,
                unresolvedVirtualCallParameterTypesSize = ToSize(s[26].Size),
                unresolvedVirtualCallParameterRangesOffset = (int)s[27].Offset,
                unresolvedVirtualCallParameterRangesSize = ToSize(s[27].Size),
                exportedTypeDefinitionsOffset = (int)s[30].Offset,
                exportedTypeDefinitionsSize = ToSize(s[30].Size),
            };
        }

        private Il2CppImageDefinition[] ReadAceImageDefinitions(uint addr, uint count)
        {
            Position = addr;
            var result = new Il2CppImageDefinition[count];
            for (var i = 0; i < result.Length; i++)
            {
                var entry = (ulong)addr + (ulong)i * 36ul;
                Position = entry;
                var imageDef = new Il2CppImageDefinition
                {
                    nameIndex = ReadUInt32(),
                    assemblyIndex = ReadInt32()
                };
                var packedType = ReadUInt32();
                imageDef.typeStart = (int)(packedType & 0xffff);
                imageDef.typeCount = packedType >> 16;
                Position = entry + 0x14;
                imageDef.entryPointIndex = ReadInt32();
                imageDef.token = ReadUInt32();
                imageDef.exportedTypeStart = -1;
                imageDef.exportedTypeCount = 0;
                imageDef.customAttributeStart = -1;
                imageDef.customAttributeCount = 0;
                result[i] = imageDef;
            }
            return result;
        }

        private Il2CppAssemblyDefinition[] ReadAceAssemblyDefinitions(uint addr, uint count)
        {
            var result = new Il2CppAssemblyDefinition[count];
            for (var i = 0; i < result.Length; i++)
            {
                var entry = (ulong)addr + (ulong)i * 68ul;
                Position = entry;
                var assemblyDef = new Il2CppAssemblyDefinition
                {
                    imageIndex = ReadInt32(),
                    token = ReadUInt32(),
                    customAttributeIndex = ReadInt32(),
                    referencedAssemblyStart = ReadInt32(),
                    referencedAssemblyCount = ReadInt32(),
                    aname = new Il2CppAssemblyNameDefinition
                    {
                        nameIndex = ReadUInt32(),
                        cultureIndex = ReadUInt32(),
                        hashValueIndex = ReadInt32(),
                        publicKeyIndex = ReadUInt32(),
                        hash_alg = ReadUInt32(),
                        hash_len = ReadInt32(),
                        flags = ReadUInt32(),
                        major = ReadInt32(),
                        minor = ReadInt32(),
                        build = ReadInt32(),
                        revision = ReadInt32(),
                        public_key_token = ReadBytes(8)
                    }
                };
                result[i] = assemblyDef;
            }
            return result;
        }

        private Il2CppTypeDefinition[] ReadAceTypeDefinitions(uint addr, uint count)
        {
            Position = addr;
            var result = new Il2CppTypeDefinition[count];
            for (var i = 0; i < result.Length; i++)
            {
                var entry = (ulong)addr + (ulong)i * 82ul;
                Position = entry;
                var typeDef = new Il2CppTypeDefinition
                {
                    nameIndex = ReadUInt32(),
                    namespaceIndex = ReadUInt32(),
                    byvalTypeIndex = ReadInt32(),
                    byrefTypeIndex = -1,
                    declaringTypeIndex = ReadInt32(),
                    parentIndex = ReadInt32()
                };
                Position = entry + 0x18;
                typeDef.flags = ReadUInt16();
                typeDef.fieldStart = ReadUInt16();
                // methodStart 在 entry+0x1c 被压成 16 位会溢出，改为读完 method 表后用前缀和重建（见下方循环）
                Position = entry + 0x32;
                typeDef.vtableStart = ReadUInt16();
                Position = entry + 0x3a;
                typeDef.method_count = ReadUInt16();
                typeDef.property_count = ReadUInt16();
                typeDef.field_count = ReadUInt16();
                typeDef.event_count = ReadUInt16();
                typeDef.nested_type_count = ReadUInt16();
                typeDef.vtable_count = ReadUInt16();
                typeDef.interfaces_count = ReadUInt16();
                typeDef.interface_offsets_count = ReadUInt16();
                Position = entry + 0x4a;
                typeDef.bitfield = ReadUInt32();
                Position = entry + 0x4e;
                typeDef.token = ReadUInt32();
                typeDef.customAttributeIndex = -1;
                typeDef.elementTypeIndex = -1;
                typeDef.rgctxStartIndex = -1;
                typeDef.rgctxCount = 0;
                typeDef.genericContainerIndex = -1;
                typeDef.delegateWrapperFromManagedToNativeIndex = -1;
                typeDef.marshalingFunctionsIndex = -1;
                typeDef.ccwFunctionIndex = -1;
                typeDef.guidIndex = -1;
                typeDef.eventStart = -1;
                typeDef.propertyStart = -1;
                typeDef.nestedTypesStart = -1;
                typeDef.interfacesStart = -1;
                typeDef.interfaceOffsetsStart = -1;
                result[i] = typeDef;
            }
            // ACE 把 typeDef.methodStart 压缩成 16 位（entry+0x1c 高 16 位），方法总数 >65535 时溢出饱和，
            // 导致各 type 的方法区间坍缩重叠。method 表按 type 连续分块、method_count 又准确，
            // 故用前缀和重建 methodStart（与标准 IL2CPP 的累积语义一致）。
            int methodAcc = 0;
            for (var i = 0; i < result.Length; i++)
            {
                result[i].methodStart = methodAcc;
                methodAcc += result[i].method_count;
            }
            return result;
        }

        private Il2CppMethodDefinition[] ReadAceMethodDefinitions(uint addr, uint count)
        {
            Position = addr;
            var result = new Il2CppMethodDefinition[count];
            var parameterStart = 0;
            for (var i = 0; i < result.Length; i++)
            {
                var entry = (ulong)addr + (ulong)i * 32ul;
                Position = entry;
                var method = new Il2CppMethodDefinition
                {
                    nameIndex = ReadUInt32()
                };
                var packedDeclaringType = ReadUInt32();
                method.declaringType = (int)(packedDeclaringType & 0xffff);
                method.returnType = -1;
                Position = entry + 0x10;
                method.genericContainerIndex = ReadInt32();
                method.token = ReadUInt32();
                method.flags = ReadUInt16();
                method.iflags = ReadUInt16();
                method.slot = ReadUInt16();
                Position = entry + 0x1c;
                var packedParamCount = ReadUInt32();
                method.parameterCount = (ushort)(packedParamCount >> 16);
                method.parameterStart = parameterStart;
                parameterStart += method.parameterCount;
                method.customAttributeIndex = -1;
                method.methodIndex = -1;
                method.invokerIndex = -1;
                method.delegateWrapperIndex = -1;
                method.rgctxStartIndex = -1;
                method.rgctxCount = 0;
                result[i] = method;
            }
            return result;
        }

        private Il2CppFieldDefinition[] ReadAceFieldDefinitions(uint addr, uint count)
        {
            Position = addr;
            var result = new Il2CppFieldDefinition[count];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = new Il2CppFieldDefinition
                {
                    nameIndex = ReadUInt32(),
                    typeIndex = ReadInt32(),
                    token = ReadUInt32(),
                    customAttributeIndex = -1
                };
            }
            return result;
        }

        private Il2CppParameterDefinition[] ReadAceParameterDefinitions(uint addr, uint count)
        {
            Position = addr;
            var result = new Il2CppParameterDefinition[count];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = new Il2CppParameterDefinition
                {
                    nameIndex = ReadUInt32(),
                    token = ReadUInt32(),
                    typeIndex = ReadInt32(),
                    customAttributeIndex = -1
                };
            }
            return result;
        }

        private Il2CppGenericParameter[] ReadAceGenericParameters(uint addr, uint count)
        {
            Position = addr;
            var result = new Il2CppGenericParameter[count];
            for (var i = 0; i < result.Length; i++)
            {
                var entry = (ulong)addr + (ulong)i * 14ul;
                Position = entry;
                result[i] = new Il2CppGenericParameter
                {
                    ownerIndex = ReadUInt16(),
                    nameIndex = ReadUInt32(),
                    constraintsStart = ReadInt16(),
                    constraintsCount = ReadInt16(),
                    num = ReadUInt16(),
                    flags = ReadUInt16()
                };
            }
            return result;
        }

        private Il2CppStringLiteral[] ReadAceStringLiterals(uint addr, uint count, int dataSize)
        {
            Position = addr;
            var offsets = new uint[count + 1];
            for (var i = 0; i < count; i++)
            {
                offsets[i] = ReadUInt32();
            }
            offsets[count] = (uint)dataSize;
            var result = new Il2CppStringLiteral[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = new Il2CppStringLiteral
                {
                    dataIndex = (int)offsets[i],
                    length = offsets[i + 1] - offsets[i]
                };
            }
            return result;
        }

        private readonly record struct AceSection(uint Offset, uint Size, uint Count);

        private T[] ReadMetadataClassArray<T>(uint addr, int count) where T : new()
        {
            return ReadClassArray<T>(addr, count / SizeOf(typeof(T)));
        }

        public bool GetFieldDefaultValueFromIndex(int index, out Il2CppFieldDefaultValue value)
        {
            return fieldDefaultValuesDic.TryGetValue(index, out value);
        }

        public bool GetParameterDefaultValueFromIndex(int index, out Il2CppParameterDefaultValue value)
        {
            return parameterDefaultValuesDic.TryGetValue(index, out value);
        }

        public uint GetDefaultValueFromIndex(int index)
        {
            return (uint)(header.fieldAndParameterDefaultValueDataOffset + index);
        }

        public string GetStringFromIndex(uint index)
        {
            if (!stringCache.TryGetValue(index, out var result))
            {
                result = ReadStringToNull(header.stringOffset + index);
                stringCache.Add(index, result);
            }
            return result;
        }

        public int GetCustomAttributeIndex(Il2CppImageDefinition imageDef, int customAttributeIndex, uint token)
        {
            if (Version > 24)
            {
                if (attributeTypeRangesDic[imageDef].TryGetValue(token, out var index))
                {
                    return index;
                }
                else
                {
                    return -1;
                }
            }
            else
            {
                return customAttributeIndex;
            }
        }

        public string GetStringLiteralFromIndex(uint index)
        {
            var stringLiteral = stringLiterals[index];
            Position = (uint)(header.stringLiteralDataOffset + stringLiteral.dataIndex);
            return Encoding.UTF8.GetString(ReadBytes((int)stringLiteral.length));
        }

        private void ProcessingMetadataUsage()
        {
            metadataUsageDic = new Dictionary<Il2CppMetadataUsage, SortedDictionary<uint, uint>>();
            for (uint i = 1; i <= 6; i++)
            {
                metadataUsageDic[(Il2CppMetadataUsage)i] = new SortedDictionary<uint, uint>();
            }
            foreach (var metadataUsageList in metadataUsageLists)
            {
                for (int i = 0; i < metadataUsageList.count; i++)
                {
                    var offset = metadataUsageList.start + i;
                    if (offset >= metadataUsagePairs.Length)
                    {
                        continue;
                    }
                    var metadataUsagePair = metadataUsagePairs[offset];
                    var usage = GetEncodedIndexType(metadataUsagePair.encodedSourceIndex);
                    var decodedIndex = GetDecodedMethodIndex(metadataUsagePair.encodedSourceIndex);
                    metadataUsageDic[(Il2CppMetadataUsage)usage][metadataUsagePair.destinationIndex] = decodedIndex;
                }
            }
            //metadataUsagesCount = metadataUsagePairs.Max(x => x.destinationIndex) + 1;
            metadataUsagesCount = metadataUsageDic.Max(x => x.Value.Select(y => y.Key).DefaultIfEmpty().Max()) + 1;
        }

        public static uint GetEncodedIndexType(uint index)
        {
            return (index & 0xE0000000) >> 29;
        }

        public uint GetDecodedMethodIndex(uint index)
        {
            if (Version >= 27)
            {
                return (index & 0x1FFFFFFEU) >> 1;
            }
            return index & 0x1FFFFFFFU;
        }

        public int SizeOf(Type type)
        {
            var size = 0;
            foreach (var i in type.GetFields())
            {
                var attr = (VersionAttribute)Attribute.GetCustomAttribute(i, typeof(VersionAttribute));
                if (attr != null)
                {
                    if (Version < attr.Min || Version > attr.Max)
                        continue;
                }
                var fieldType = i.FieldType;
                if (fieldType.IsPrimitive)
                {
                    size += GetPrimitiveTypeSize(fieldType.Name);
                }
                else if (fieldType.IsEnum)
                {
                    var e = fieldType.GetField("value__").FieldType;
                    size += GetPrimitiveTypeSize(e.Name);
                }
                else if (fieldType.IsArray)
                {
                    var arrayLengthAttribute = i.GetCustomAttribute<ArrayLengthAttribute>();
                    size += arrayLengthAttribute.Length;
                }
                else
                {
                    size += SizeOf(fieldType);
                }
            }
            return size;

            static int GetPrimitiveTypeSize(string name)
            {
                return name switch
                {
                    "Int32" or "UInt32" => 4,
                    "Int16" or "UInt16" => 2,
                    _ => 0,
                };
            }
        }
    }
}
