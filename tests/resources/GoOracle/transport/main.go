package main

import (
	"fmt"
	"os"
	"strconv"
)

func main() {
	switch os.Args[1] {
	case "certificates":
		certificates(os.Args[2])
	case "serve":
		serve(os.Args[2], os.Args[3], os.Args[4])
	case "verify":
		verify(os.Args[2], os.Args[3], os.Args[4])
	case "measure":
		size, err := strconv.Atoi(os.Args[5])
		check(err)
		workers, err := strconv.Atoi(os.Args[6])
		check(err)
		measure(os.Args[2], os.Args[3], os.Args[4], size, workers, len(os.Args) > 7 && os.Args[7] == "separate")
	default:
		panic(fmt.Sprintf("unknown command %q", os.Args[1]))
	}
}
