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

// Deterministic PRNG (mulberry32) so that every report reproduces the same intervals.
function rng(seed) {
    let a = seed >>> 0;
    return () => {
        a = (a + 0x6d2b79f5) >>> 0;
        let t = a;
        t = Math.imul(t ^ (t >>> 15), t | 1);
        t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

const resample = (values, next) => values.map(() => values[Math.floor(next() * values.length)]);

// Percentile bootstrap of median(a)/median(b), independent resampling of both samples.
// Returns { estimate, low, high } for the given confidence level.
export function bootstrapMedianRatio(a, b, { iterations = 10000, confidence = 0.95, seed = 20261021 } = {}) {
    const next = rng(seed);
    const ratios = new Float64Array(iterations);
    for (let i = 0; i < iterations; i++) ratios[i] = median(resample(a, next)) / median(resample(b, next));
    ratios.sort();
    const tail = (1 - confidence) / 2;
    return { estimate: median(a) / median(b), low: ratios[Math.floor(tail * iterations)], high: ratios[Math.ceil((1 - tail) * iterations) - 1] };
}

// Same for the geometric mean over several repositories: pairs = [[a1, b1], [a2, b2], ...].
export function bootstrapGeometricMeanRatio(pairs, { iterations = 10000, confidence = 0.95, seed = 20261021 } = {}) {
    const next = rng(seed);
    const geo = rs => Math.exp(rs.reduce((s, r) => s + Math.log(r), 0) / rs.length);
    const values = new Float64Array(iterations);
    for (let i = 0; i < iterations; i++) values[i] = geo(pairs.map(([a, b]) => median(resample(a, next)) / median(resample(b, next))));
    values.sort();
    const tail = (1 - confidence) / 2;
    return { estimate: geo(pairs.map(([a, b]) => median(a) / median(b))), low: values[Math.floor(tail * iterations)], high: values[Math.ceil((1 - tail) * iterations) - 1] };
}
