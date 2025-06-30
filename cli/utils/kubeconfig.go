// Copyright 2024 The Drasi Authors.
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

package utils

import (
	"fmt"
	"net/url"
	"strings"

	"k8s.io/client-go/tools/clientcmd"
)

// FixKubeconfigServerURL fixes problematic server URLs in kubeconfig that don't resolve on Windows
// This replaces host.docker.internal and 0.0.0.0 with localhost while preserving the port
func FixKubeconfigServerURL(kubeconfigBytes []byte) ([]byte, error) {
	config, err := clientcmd.Load(kubeconfigBytes)
	if err != nil {
		return nil, fmt.Errorf("error loading kubeconfig: %w", err)
	}

	// Modify server URL to use localhost for Windows compatibility
	for k, cluster := range config.Clusters {
		serverURL, err := url.Parse(cluster.Server)
		if err != nil {
			return nil, fmt.Errorf("error parsing server URL: %w", err)
		}

		// Extract port and update to use localhost for Windows Docker Desktop compatibility
		// This handles cases where k3d uses host.docker.internal or 0.0.0.0 which may not resolve on Windows
		hostParts := strings.Split(serverURL.Host, ":")
		if len(hostParts) == 2 {
			port := hostParts[1]
			// Replace problematic hostnames with localhost
			hostname := hostParts[0]
			if hostname == "host.docker.internal" || hostname == "0.0.0.0" {
				serverURL.Host = "localhost:" + port
				cluster.Server = serverURL.String()
				config.Clusters[k] = cluster
			}
		}
	}

	// Convert back to bytes
	return clientcmd.Write(*config)
}