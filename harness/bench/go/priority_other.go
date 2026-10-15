// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0

//go:build !windows

package main

func raisePriority() string { panic("NEVERSHARP_PRIORITY=high is implemented for Windows only") }

func currentPriority() string { return "unknown" }
