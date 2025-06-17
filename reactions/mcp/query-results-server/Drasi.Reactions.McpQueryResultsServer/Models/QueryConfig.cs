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

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Drasi.Reactions.McpQueryResultsServer.Models;

public class QueryConfig : IValidatableObject
{
    [Required]
    [JsonPropertyName("keyField")]
    public string KeyField { get; set; } = string.Empty;
    
    [JsonPropertyName("resourceContentType")]
    public string ResourceContentType { get; set; } = "application/json";
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(KeyField))
        {
            yield return new ValidationResult(
                "KeyField is required for identifying unique entries", 
                new[] { nameof(KeyField) });
        }
        
        // Validate content type
        var validContentTypes = new[] { "application/json", "text/plain", "application/xml" };
        if (!validContentTypes.Contains(ResourceContentType))
        {
            yield return new ValidationResult(
                $"ResourceContentType must be one of: {string.Join(", ", validContentTypes)}", 
                new[] { nameof(ResourceContentType) });
        }
    }
}