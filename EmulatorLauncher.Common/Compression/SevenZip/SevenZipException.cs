﻿using System;
using System.Runtime.Serialization;

namespace EmulatorLauncher.Common.Compression.SevenZip
{
    public class SevenZipException : Exception
    {
        public SevenZipException()
        {
        }

        public SevenZipException(string message) : base(message)
        {
        }

        public SevenZipException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected SevenZipException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}