// Copyright 2025 The Drasi Authors.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Drasi.Reactions.Mcp.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Drasi.Reactions.Mcp.Services;

public class ErrorStateHandler : IErrorStateHandler
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ErrorStateHandler> _logger;

    public ErrorStateHandler(
        IHostApplicationLifetime lifetime,
        ILogger<ErrorStateHandler> logger)
    {
        _lifetime = lifetime;
        _logger = logger;
    }

    public Task HandleFatalErrorAsync(Exception exception, string? message = null)
    {
        _logger.LogCritical(exception, "Fatal error: {Message}", message ?? exception.Message);
        
        // Set exit code to indicate failure
        Environment.ExitCode = 1;

        Reaction.SDK.Reaction<McpQueryConfig>.TerminateWithError(message ?? exception.Message);
        
        // Trigger application shutdown
        _lifetime.StopApplication();
        
        return Task.CompletedTask;
    }
}