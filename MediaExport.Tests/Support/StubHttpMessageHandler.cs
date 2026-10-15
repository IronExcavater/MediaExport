// Copyright (C) 2026 Niclas Rogulski, Andrew Shillabeer, Ezekiel Paterno. All rights reserved.
// SPDX-License-Identifier: MPL-2.0

using System.Net;

namespace MediaExport.Tests.Support;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    public byte[] Content { get; set; } = [];
    public Exception? Failure { get; set; }
    public Func<CancellationToken, Task<HttpResponseMessage>>? Response { get; set; }
    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        if (Response is not null)
            return Response(cancellationToken);
        if (Failure is not null)
            return Task.FromException<HttpResponseMessage>(Failure);

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Content)
        });
    }
}
