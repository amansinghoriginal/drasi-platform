package installers

import (
	"testing"

	"drasi.io/cli/utils"
	"k8s.io/client-go/tools/clientcmd"
	"k8s.io/client-go/tools/clientcmd/api"
)

func TestServerURLReplacement(t *testing.T) {
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
			kubeconfig, err := clientcmd.Write(*config)
			if err != nil {
				t.Fatalf("Failed to write kubeconfig: %v", err)
			}

			// Use the shared function to fix the kubeconfig
			fixedKubeconfig, err := utils.FixKubeconfigServerURL(kubeconfig)
			if err != nil {
				t.Fatalf("FixKubeconfigServerURL failed: %v", err)
			}

			// Parse the fixed kubeconfig
			dockerConfig, err := clientcmd.Load(fixedKubeconfig)
			if err != nil {
				t.Fatalf("Failed to load fixed kubeconfig: %v", err)
			}

			// Verify the result
			cluster := dockerConfig.Clusters["test-cluster"]
			if cluster.Server != tt.expectedServer {
				t.Errorf("Expected server URL %q, got %q", tt.expectedServer, cluster.Server)
			}

			// Verify if change was expected
			changed := cluster.Server != tt.originalServer
			if changed != tt.shouldChange {
				if tt.shouldChange {
					t.Errorf("Expected server URL to change from %q, but it remained unchanged", tt.originalServer)
				} else {
					t.Errorf("Expected server URL to remain %q, but it changed to %q", tt.originalServer, cluster.Server)
				}
			}
		})
	}
}

func TestMergeDockerKubeConfigWithHostDockerInternal(t *testing.T) {
	// Create a mock kubeconfig similar to what k3d generates on Windows
	config := &api.Config{
		Clusters: map[string]*api.Cluster{
			"default": {
				Server:                   "https://host.docker.internal:50662",
				CertificateAuthorityData: []byte("test-ca-data"),
			},
		},
		AuthInfos: map[string]*api.AuthInfo{
			"default": {
				ClientCertificateData: []byte("test-cert-data"),
				ClientKeyData:         []byte("test-key-data"),
			},
		},
		Contexts: map[string]*api.Context{
			"default": {
				Cluster:  "default",
				AuthInfo: "default",
			},
		},
		CurrentContext: "default",
	}

	// Convert to bytes as the function expects
	kubeconfig, err := clientcmd.Write(*config)
	if err != nil {
		t.Fatalf("Failed to write kubeconfig: %v", err)
	}

	// Use the shared function to fix the kubeconfig
	fixedKubeconfig, err := utils.FixKubeconfigServerURL(kubeconfig)
	if err != nil {
		t.Fatalf("FixKubeconfigServerURL failed: %v", err)
	}

	// Parse the fixed kubeconfig
	dockerConfig, err := clientcmd.Load(fixedKubeconfig)
	if err != nil {
		t.Fatalf("Failed to load fixed kubeconfig: %v", err)
	}

	// Verify the server URL was replaced
	cluster := dockerConfig.Clusters["default"]
	expectedServer := "https://localhost:50662"
	if cluster.Server != expectedServer {
		t.Errorf("Expected server URL to be %q, got %q", expectedServer, cluster.Server)
	}
}
