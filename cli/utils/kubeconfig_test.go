package utils

import (
	"testing"

	"k8s.io/client-go/tools/clientcmd"
	"k8s.io/client-go/tools/clientcmd/api"
)

func TestFixKubeconfigServerURL(t *testing.T) {
	tests := []struct {
		name           string
		originalServer string
		expectedServer string
		shouldChange   bool
	}{
		{
			name:           "host.docker.internal should be replaced with localhost",
			originalServer: "https://host.docker.internal:50662",
			expectedServer: "https://localhost:50662",
			shouldChange:   true,
		},
		{
			name:           "0.0.0.0 should be replaced with localhost",
			originalServer: "https://0.0.0.0:6443",
			expectedServer: "https://localhost:6443",
			shouldChange:   true,
		},
		{
			name:           "127.0.0.1 should not be changed",
			originalServer: "https://127.0.0.1:6443",
			expectedServer: "https://127.0.0.1:6443",
			shouldChange:   false,
		},
		{
			name:           "localhost should not be changed",
			originalServer: "https://localhost:6443",
			expectedServer: "https://localhost:6443",
			shouldChange:   false,
		},
		{
			name:           "external hostname should not be changed",
			originalServer: "https://my-cluster.example.com:6443",
			expectedServer: "https://my-cluster.example.com:6443",
			shouldChange:   false,
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			// Create a kubeconfig with the test server URL
			config := &api.Config{
				Clusters: map[string]*api.Cluster{
					"test-cluster": {
						Server: tt.originalServer,
					},
				},
				Contexts: map[string]*api.Context{
					"test-context": {
						Cluster: "test-cluster",
					},
				},
				CurrentContext: "test-context",
			}

			// Convert to bytes
			originalBytes, err := clientcmd.Write(*config)
			if err != nil {
				t.Fatalf("Failed to write original config: %v", err)
			}

			// Apply the fix
			fixedBytes, err := FixKubeconfigServerURL(originalBytes)
			if err != nil {
				t.Fatalf("FixKubeconfigServerURL failed: %v", err)
			}

			// Parse the fixed config
			fixedConfig, err := clientcmd.Load(fixedBytes)
			if err != nil {
				t.Fatalf("Failed to load fixed config: %v", err)
			}

			// Check the result
			cluster := fixedConfig.Clusters["test-cluster"]
			if cluster == nil {
				t.Fatal("Cluster not found in fixed config")
			}

			if cluster.Server != tt.expectedServer {
				t.Errorf("Expected server URL %s, got %s", tt.expectedServer, cluster.Server)
			}

			// Verify if change occurred as expected
			changed := cluster.Server != tt.originalServer
			if changed != tt.shouldChange {
				t.Errorf("Expected shouldChange=%v, but change occurred=%v", tt.shouldChange, changed)
			}
		})
	}
}

func TestFixKubeconfigServerURLIntegration(t *testing.T) {
	// Test the specific k3d scenario mentioned in the issue
	kubeconfigYAML := `apiVersion: v1
clusters:
- cluster:
    certificate-authority-data: LS0tLS1CRUdJTiBDRVJUSUZJQ0FURS0tLS0t
    server: https://host.docker.internal:50662
  name: default
contexts:
- context:
    cluster: default
    user: default
  name: default
current-context: default
kind: Config
preferences: {}
users:
- name: default
  user:
    client-certificate-data: LS0tLS1CRUdJTiBDRVJUSUZJQ0FURS0tLS0t
    client-key-data: LS0tLS1CRUdJTiBQUklWQVRFIEtFWS0tLS0t
`

	fixedBytes, err := FixKubeconfigServerURL([]byte(kubeconfigYAML))
	if err != nil {
		t.Fatalf("FixKubeconfigServerURL failed: %v", err)
	}

	fixedConfig, err := clientcmd.Load(fixedBytes)
	if err != nil {
		t.Fatalf("Failed to load fixed config: %v", err)
	}

	cluster := fixedConfig.Clusters["default"]
	if cluster == nil {
		t.Fatal("Default cluster not found in fixed config")
	}

	expectedServer := "https://localhost:50662"
	if cluster.Server != expectedServer {
		t.Errorf("Expected server URL %s, got %s", expectedServer, cluster.Server)
	}
}