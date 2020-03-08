﻿using System.Collections.Generic;
using PeNet.Structures;
using PeNet.Utilities;
using static PeNet.Constants;

namespace PeNet.Parser
{
    internal class ImportedFunctionsParser : SafeParser<ImportFunction[]>
    {
        private readonly IMAGE_IMPORT_DESCRIPTOR[]? _importDescriptors;
        private readonly bool _is64Bit;
        private readonly IMAGE_SECTION_HEADER[] _sectionHeaders;
        private readonly IMAGE_DATA_DIRECTORY[] _dataDirectories;

        internal ImportedFunctionsParser(
            IRawFile peFile,
            IMAGE_IMPORT_DESCRIPTOR[]? importDescriptors,
            IMAGE_SECTION_HEADER[] sectionHeaders,
            IMAGE_DATA_DIRECTORY[] dataDirectories,
            bool is64Bit) :
                base(peFile, 0)
        {
            _importDescriptors = importDescriptors;
            _sectionHeaders = sectionHeaders;
            _dataDirectories = dataDirectories;
            _is64Bit = is64Bit;
        }

        protected override ImportFunction[]? ParseTarget()
        {
            if (_importDescriptors == null)
                return null;

            var impFuncs = new List<ImportFunction>();
            var sizeOfThunk = (uint) (_is64Bit ? 0x8 : 0x4); // Size of IMAGE_THUNK_DATA
            var ordinalBit = _is64Bit ? 0x8000000000000000 : 0x80000000;
            var ordinalMask = (ulong) (_is64Bit ? 0x7FFFFFFFFFFFFFFF : 0x7FFFFFFF);
            var iat = _dataDirectories[(int)DataDirectoryIndex.IAT];

            foreach (var idesc in _importDescriptors)
            {
                var dllAdr = idesc.Name.RVAtoFileMapping(_sectionHeaders);
                var dll = PeFile.ReadAsciiString(dllAdr);
                if (IsModuleNameTooLong(dll))
                    continue;
                var tmpAdr = idesc.OriginalFirstThunk != 0 ? idesc.OriginalFirstThunk : idesc.FirstThunk;
                if (tmpAdr == 0)
                    continue;

                var thunkAdr = tmpAdr.RVAtoFileMapping(_sectionHeaders);
                uint round = 0;
                while (true)
                {
                    var t = new IMAGE_THUNK_DATA(PeFile, thunkAdr + round*sizeOfThunk, _is64Bit);
                    var iatOffset = idesc.FirstThunk + round * sizeOfThunk - iat.VirtualAddress;

                    if (t.AddressOfData == 0)
                        break;

                    // Check if import by name or by ordinal.
                    // If it is an import by ordinal, the most significant bit of "Ordinal" is "1" and the ordinal can
                    // be extracted from the least significant bits.
                    // Else it is an import by name and the link to the IMAGE_IMPORT_BY_NAME has to be followed

                    if ((t.Ordinal & ordinalBit) == ordinalBit) // Import by ordinal
                    {
                        impFuncs.Add(new ImportFunction(null, dll, (ushort) (t.Ordinal & ordinalMask), iatOffset) );
                    }
                    else // Import by name
                    {
                        var ibn = new IMAGE_IMPORT_BY_NAME(PeFile,
                            ((uint) t.AddressOfData).RVAtoFileMapping(_sectionHeaders));
                        impFuncs.Add(new ImportFunction(ibn.Name, dll, ibn.Hint, iatOffset));
                    }

                    round++;
                }
            }


            return impFuncs.ToArray();
        }

        private bool IsModuleNameTooLong(string dllName)
        {
            return dllName.Length > 256;
        }
    }
}