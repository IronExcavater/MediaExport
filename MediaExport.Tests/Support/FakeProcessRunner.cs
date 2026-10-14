// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using MediaExport.Core.Tooling;

namespace MediaExport.Tests.Support;

internal sealed class FakeProcessRunner : IProcessRunner
{
    public ProcessResult Result { get; set; } = new(0, "1.0", "");
    public Exception? Failure { get; set; }
    public List<(string FileName, string Arguments)> Requests { get; } = [];

    public Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken = default)
    {
        Requests.Add((fileName, arguments));
        if (Failure is not null)
            return Task.FromException<ProcessResult>(Failure);

        return Task.FromResult(Result);
    }
}
