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

using Drasi.Reaction.SDK.Services;
using Drasi.Reactions.McpServer.Models;
using Microsoft.Extensions.Logging;

namespace Drasi.Reactions.McpServer.Services;

public interface IConfigValidationService
{
    Task<bool> ValidateAll();
}

public class ConfigValidationService : IConfigValidationService
{
    private readonly IQueryConfigService _queryConfigService;
    private readonly ILogger<ConfigValidationService> _logger;
    private readonly HashSet<string> _validMimeTypes = new()
    {
        "application/json",
        "text/plain",
        "application/xml",
        "text/html",
        "text/csv"
    };

    public ConfigValidationService(
        IQueryConfigService queryConfigService,
        ILogger<ConfigValidationService> logger)
    {
        _queryConfigService = queryConfigService;
        _logger = logger;
    }

    public async Task<bool> ValidateAll()
    {
        var isValid = true;
        var queryNames = _queryConfigService.GetQueryNames();

        if (!queryNames.Any())
        {
            _logger.LogWarning("No queries configured");
            return false;
        }

        foreach (var queryName in queryNames)
        {
            try
            {
                var config = _queryConfigService.GetQueryConfig<QueryConfig>(queryName);
                
                if (config == null)
                {
                    _logger.LogError("Query {QueryName} has null configuration", queryName);
                    isValid = false;
                    continue;
                }

                // Validate keyField
                if (string.IsNullOrWhiteSpace(config.KeyField))
                {
                    _logger.LogError("Query {QueryName} missing required field: keyField", queryName);
                    isValid = false;
                }

                // Validate resourceContentType
                if (string.IsNullOrWhiteSpace(config.ResourceContentType))
                {
                    _logger.LogError("Query {QueryName} missing required field: resourceContentType", queryName);
                    isValid = false;
                }
                else if (!_validMimeTypes.Contains(config.ResourceContentType))
                {
                    _logger.LogWarning(
                        "Query {QueryName} has non-standard MIME type: {MimeType}",
                        queryName, config.ResourceContentType);
                }

                // Log successful validation
                if (isValid)
                {
                    _logger.LogDebug(
                        "Query {QueryName} configuration is valid (keyField: {KeyField}, contentType: {ContentType})",
                        queryName, config.KeyField, config.ResourceContentType);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate query {QueryName}", queryName);
                isValid = false;
            }
        }

        await Task.CompletedTask;
        return isValid;
    }
}