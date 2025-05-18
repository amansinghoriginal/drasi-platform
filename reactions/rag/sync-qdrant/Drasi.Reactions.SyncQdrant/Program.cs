using Azure;
using Drasi.Reaction.SDK;
using Drasi.Reactions.SyncQdrant;
using Microsoft.Extensions.DependencyInjection;
using Azure.AI.OpenAI;
using Qdrant.Client;


var reaction = new ReactionBuilder<QueryConfig>()
    .UseChangeEventHandler<QdrantChangeEventHandler>()
    .UseJsonQueryConfig()
    .ConfigureServices(services => 
    {
        services.AddHttpClient("DrasiManagementApiClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        // Hardcoded Qdrant connection details
        string qdrantHost = "qdrant-service.default.svc.cluster.local";
        int qdrantPort = 6334;
        bool qdrantHttps = false;

        services.AddSingleton(new QdrantClient(qdrantHost, qdrantPort, https: qdrantHttps));
        services.AddSingleton(new AzureOpenAIClient(
            new Uri("https://drasi-rag-demo.openai.azure.com/"),
            new AzureKeyCredential("<PASTE YOUR AZURE OPENAI KEY HERE>")));

        services.AddSingleton<IQuerySyncStateManager, QuerySyncStateManager>();

        services.AddHostedService<QueryInitializationService>();
    })
    .Build();

try
{
    await reaction.StartAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Fatal error starting reaction: {ex.Message}");
    Environment.Exit(1);
}