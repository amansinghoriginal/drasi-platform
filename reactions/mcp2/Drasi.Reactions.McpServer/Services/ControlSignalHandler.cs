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

using Drasi.Reaction.SDK;
using Drasi.Reaction.SDK.Models.QueryOutput;
using Drasi.Reactions.McpServer.Models;
using Microsoft.Extensions.Logging;

namespace Drasi.Reactions.McpServer.Services;

public class ControlSignalHandler : IControlEventHandler<QueryConfig>
{
    private readonly ILogger<ControlSignalHandler> _logger;

    public ControlSignalHandler(ILogger<ControlSignalHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleControlSignal(ControlEvent evt, QueryConfig? queryConfig)
    {
        _logger.LogInformation(
            "Received control signal {SignalKind} for query {QueryId}",
            evt.ControlSignal?.GetType().Name ?? "Unknown",
            evt.QueryId);

        var signalKind = evt.ControlSignal?.Kind switch
        {
            ControlSignalKind.BootstrapStarted => "BootstrapStarted",
            ControlSignalKind.BootstrapCompleted => "BootstrapCompleted",
            ControlSignalKind.Running => "Running",
            ControlSignalKind.Stopped => "Stopped",
            ControlSignalKind.Deleted => "Deleted",
            _ => "Unknown"
        };

        switch (signalKind)
        {
            case "BootstrapStarted":
                _logger.LogInformation("Query {QueryId} is starting bootstrap", evt.QueryId);
                break;
            case "BootstrapCompleted":
                _logger.LogInformation("Query {QueryId} completed bootstrap", evt.QueryId);
                break;
            case "Running":
                _logger.LogInformation("Query {QueryId} is running", evt.QueryId);
                break;
            case "Stopped":
                _logger.LogWarning("Query {QueryId} has stopped", evt.QueryId);
                break;
            case "Deleted":
                _logger.LogWarning("Query {QueryId} has been deleted", evt.QueryId);
                break;
            default:
                _logger.LogWarning("Unknown control signal for query {QueryId}", evt.QueryId);
                break;
        }

        await Task.CompletedTask;
    }
}