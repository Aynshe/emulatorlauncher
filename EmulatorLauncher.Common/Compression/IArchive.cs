﻿using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace EmulatorLauncher.Common.Compression
{
    public interface IArchive : IDisposable
    {
        IArchiveEntry[] Entries { get; }
        void Extract(string destination, string fileNameToExtract = null, ProgressChangedEventHandler progress = null, bool keepFolder = false);
    }

   
}