// Copyright 2026 Andre Kaufmann
// SPDX-License-Identifier: Apache-2.0

export function median(values) {
    const s = [...values].sort((a, b) => a - b);
    const m = s.length >> 1;
    return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2;
}

// Median absolute deviation, unscaled (no 1.4826 consistency factor).
export function mad(values) {
    const m = median(values);
    return median(values.map(v => Math.abs(v - m)));
}
