// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

namespace MediaExport.Core.Tooling;

public sealed class ToolInspectionException : Exception
{
    public ToolInspectionException(string message)
        : base(message)
    {
    }

    public ToolInspectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
