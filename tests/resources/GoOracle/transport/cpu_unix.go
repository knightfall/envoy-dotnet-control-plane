//go:build linux || darwin

package main

import "syscall"

func processCPUSeconds() float64 {
	var usage syscall.Rusage
	check(syscall.Getrusage(syscall.RUSAGE_SELF, &usage))
	return float64(usage.Utime.Sec+usage.Stime.Sec) + float64(usage.Utime.Usec+usage.Stime.Usec)/1e6
}
