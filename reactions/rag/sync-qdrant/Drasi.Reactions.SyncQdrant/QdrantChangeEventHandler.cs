using Drasi.Reaction.SDK;
using Drasi.Reaction.SDK.Models.QueryOutput;

namespace Drasi.Reactions.SyncQdrant
{
    public class QdrantChangeEventHandler : IChangeEventHandler<QueryConfig>
    {
        public Task HandleChange(ChangeEvent evt, QueryConfig? queryConfig)
        {
            throw new NotImplementedException("HandleChange method is not implemented.");
        }
    }
}

