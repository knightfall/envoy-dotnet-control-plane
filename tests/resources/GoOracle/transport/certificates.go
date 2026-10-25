package main

import (
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/tls"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/pem"
	"math/big"
	"net"
	"os"
	"path/filepath"
	"time"
)

func tlsSettings(directory string, server, withIdentity bool) *tls.Config {
	authority, err := os.ReadFile(filepath.Join(directory, "ca.pem"))
	check(err)
	pool := x509.NewCertPool()
	require(pool.AppendCertsFromPEM(authority), "load CA")
	config := &tls.Config{MinVersion: tls.VersionTLS12, RootCAs: pool, ServerName: "localhost"}
	if os.Getenv("ENVOY_PROBE_TLS_PROTOCOL") == "tls13" {
		config.MinVersion = tls.VersionTLS13
		config.MaxVersion = tls.VersionTLS13
	} else {
		config.MaxVersion = tls.VersionTLS12
		config.CipherSuites = []uint16{tls.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256}
	}
	if withIdentity {
		name := "client"
		if server {
			name = "server"
		}
		certificate, err := tls.LoadX509KeyPair(filepath.Join(directory, name+".pem"), filepath.Join(directory, name+"-key.pem"))
		check(err)
		config.Certificates = []tls.Certificate{certificate}
	}
	if server {
		config.ClientCAs = pool
		config.ClientAuth = tls.RequireAndVerifyClientCert
	}
	return config
}

func certificates(directory string) {
	check(os.MkdirAll(directory, 0700))
	key, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	check(err)
	ca := &x509.Certificate{SerialNumber: big.NewInt(1), Subject: pkix.Name{CommonName: "transport-probe-ca"}, NotBefore: time.Now().Add(-time.Minute), NotAfter: time.Now().AddDate(0, 0, 1), IsCA: true, BasicConstraintsValid: true, KeyUsage: x509.KeyUsageCertSign | x509.KeyUsageDigitalSignature}
	der, err := x509.CreateCertificate(rand.Reader, ca, ca, &key.PublicKey, key)
	check(err)
	check(os.WriteFile(filepath.Join(directory, "ca.pem"), pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der}), 0600))
	for index, name := range []string{"server", "client"} {
		leafKey, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
		check(err)
		usage := x509.ExtKeyUsageClientAuth
		if name == "server" {
			usage = x509.ExtKeyUsageServerAuth
		}
		leaf := &x509.Certificate{SerialNumber: big.NewInt(int64(index + 2)), Subject: pkix.Name{CommonName: name}, NotBefore: ca.NotBefore, NotAfter: ca.NotAfter, KeyUsage: x509.KeyUsageDigitalSignature, ExtKeyUsage: []x509.ExtKeyUsage{usage}, DNSNames: []string{"localhost"}, IPAddresses: []net.IP{net.ParseIP("127.0.0.1")}}
		der, err = x509.CreateCertificate(rand.Reader, leaf, ca, &leafKey.PublicKey, key)
		check(err)
		encodedKey, err := x509.MarshalPKCS8PrivateKey(leafKey)
		check(err)
		check(os.WriteFile(filepath.Join(directory, name+".pem"), pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der}), 0600))
		check(os.WriteFile(filepath.Join(directory, name+"-key.pem"), pem.EncodeToMemory(&pem.Block{Type: "PRIVATE KEY", Bytes: encodedKey}), 0600))
	}
}
