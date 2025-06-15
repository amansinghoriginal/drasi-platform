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
using Drasi.Reactions.Mcp.Interfaces;
using Drasi.Reactions.Mcp.Models;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

namespace Drasi.Reactions.Mcp.Services;

public class QueryConfigValidationService : IQueryConfigValidationService
{
    private readonly ILogger<QueryConfigValidationService> _logger;
    private readonly IQueryConfigService _queryConfigService;

    public QueryConfigValidationService(
        ILogger<QueryConfigValidationService> logger,
        IQueryConfigService queryConfigService)
    {
        _logger = logger;
        _queryConfigService = queryConfigService;
    }

    public Task<bool> ValidateAllAsync()
    {
        var isValid = true;
        var queryNames = _queryConfigService.GetQueryNames();

        foreach (var queryName in queryNames)
        {
            try
            {
                var config = _queryConfigService.GetQueryConfig<McpQueryConfig>(queryName);
                if (config == null)
                {
                    _logger.LogError("Query {QueryName} has null configuration", queryName);
                    isValid = false;
                    continue;
                }

                // Validate using data annotations
                var validationContext = new ValidationContext(config);
                var validationResults = new List<ValidationResult>();
                
                if (!Validator.TryValidateObject(config, validationContext, validationResults, true))
                {
                    foreach (var validationResult in validationResults)
                    {
                        _logger.LogError("Query {QueryName} validation error: {Error}", 
                            queryName, validationResult.ErrorMessage);
                    }
                    isValid = false;
                }

                // Additional custom validation
                if (string.IsNullOrWhiteSpace(config.KeyField))
                {
                    _logger.LogError("Query {QueryName} has empty keyField", queryName);
                    isValid = false;
                }

                _logger.LogInformation("Query {QueryName} configuration validated successfully", queryName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate query {QueryName}", queryName);
                isValid = false;
            }
        }

        return Task.FromResult(isValid);
    }
}