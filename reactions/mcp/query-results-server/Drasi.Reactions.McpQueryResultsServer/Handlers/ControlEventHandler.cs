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
using Drasi.Reactions.McpQueryResultsServer.Models;

namespace Drasi.Reactions.McpQueryResultsServer.Handlers;

public class ControlEventHandler : IControlEventHandler<QueryConfig>
{
    private readonly ILogger<ControlEventHandler> _logger;
    
    public ControlEventHandler(ILogger<ControlEventHandler> logger)
    {
        _logger = logger;
    }
    
    public Task HandleControlSignal(ControlEvent controlEvent, QueryConfig? config)
    {
        _logger.LogInformation(
            "Received control event {Kind} for query {QueryId}",
            controlEvent.Kind,
            controlEvent.QueryId);
        
        // MCP server doesn't need special handling for control events
        // The result store maintains current state from change events
        
        return Task.CompletedTask;
    }
}