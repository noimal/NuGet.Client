﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using NuGet.Protocol.Core.Types;

namespace NuGet.PackageManagement
{
    /// <summary>
    /// The result of a cache look up. HasEntry determines if the query has already been made. 
    /// If a result has zero packages HasEntry will be true, but Packages will be empty.
    /// If the query has not been done HasEntry will be false.
    /// </summary>
    internal class GatherCacheResult
    {
        public GatherCacheResult(bool hasEntry, IReadOnlyList<SourcePackageDependencyInfo> packages)
        {
            HasEntry = hasEntry;
            Packages = packages ?? new List<SourcePackageDependencyInfo>();
        }

        /// <summary>
        /// True if an entry has been added (including a result with zero packages).
        /// This will be false if the package has not been searched for in the source.
        /// </summary>
        public bool HasEntry { get; }

        /// <summary>
        /// Cached packages
        /// </summary>
        public IReadOnlyList<SourcePackageDependencyInfo> Packages { get; }
    }
}
