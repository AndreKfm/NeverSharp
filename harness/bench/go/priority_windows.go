// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0

package main

import "golang.org/x/sys/windows"

// raisePriority sets this process to HIGH_PRIORITY_CLASS (allowed without
// administrator rights) and returns the class actually in effect.
func raisePriority() string {
	process := windows.CurrentProcess()
	if err := windows.SetPriorityClass(process, windows.HIGH_PRIORITY_CLASS); err != nil {
		panic(err)
	}
	return currentPriority()
}

func currentPriority() string {
	class, err := windows.GetPriorityClass(windows.CurrentProcess())
	if err != nil {
		panic(err)
	}
	switch class {
	case windows.HIGH_PRIORITY_CLASS:
		return "high"
	case windows.NORMAL_PRIORITY_CLASS:
		return "normal"
	default:
		return "other"
	}
}
