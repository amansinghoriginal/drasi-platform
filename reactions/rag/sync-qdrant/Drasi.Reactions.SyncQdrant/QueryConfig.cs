using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Drasi.Reactions.SyncQdrant;

public class QueryConfig : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        return Enumerable.Empty<ValidationResult>();
    }
}