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

using System.Collections.Concurrent;
using ModelContextProtocol.Server;

namespace Drasi.Reactions.McpQueryResultsServer.Services;

/// <summary>
/// Tracks active MCP server sessions for sending notifications
/// </summary>
public interface ISessionTracker
{
    void AddSession(string sessionId, IMcpServer server);
    void RemoveSession(string sessionId);
    IEnumerable<IMcpServer> GetActiveSessions();
}

public class SessionTracker : ISessionTracker
{
    private readonly ConcurrentDictionary<string, IMcpServer> _sessions = new();
    private readonly ILogger<SessionTracker> _logger;

    public SessionTracker(ILogger<SessionTracker> logger)
    {
        _logger = logger;
    }

    public void AddSession(string sessionId, IMcpServer server)
    {
        _sessions[sessionId] = server;
        _logger.LogDebug("Added MCP session {SessionId}. Total sessions: {Count}", sessionId, _sessions.Count);
    }

    public void RemoveSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out _))
        {
            _logger.LogDebug("Removed MCP session {SessionId}. Total sessions: {Count}", sessionId, _sessions.Count);
        }
    }

    public IEnumerable<IMcpServer> GetActiveSessions()
    {
        return _sessions.Values;
    }
}