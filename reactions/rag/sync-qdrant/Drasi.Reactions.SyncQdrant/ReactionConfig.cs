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

using Azure;
using Qdrant.Client.Grpc;

namespace Drasi.Reactions.SyncQdrant;

public class ReactionConfig
{
    // The Azure OpenAI resource endpoint to use.
    // This should not include model deployment or operation information.
    // For example: https://my-resource.openai.azure.com.
    required public Uri AzureOpenAIEndpoint { get; set; }

    // The API key to authenticate with the Azure OpenAI service endpoint.
    required public AzureKeyCredential AzureOpenAIKey { get; set; }

    // The deployment name of the Azure OpenAI model to use for embeddings.
    required public string EmbeddingModelName { get; set; }

    // The number of dimensions in the model's vector output.
    required public ulong ModelVectorDimensions { get; set; }

    // Hostname of the Qdrant service.
    required public string QdrantHost { get; set; }

    // The port to use for the Qdrant service.
    required public int QdrantPort { get; set; }

    // Whether to use HTTPS for the Qdrant service.
    required public bool QdrantHttps { get; set; } = false;

    // The Qdrant point ID used as key for storing sync metadata.
    required public PointId QdrantSyncMetadataPointId { get; set; }

    // Distance metric to use for the Qdrant collection for vector similarity search.
    // Can be one of the following: Cosine, Euclid, Manhattan, or Dot.
    required public Distance QdrantDistanceMetric { get; set; } = Distance.Cosine;
}