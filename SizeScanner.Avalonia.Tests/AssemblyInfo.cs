// Copyright (C) SizeScanner contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

// Avalonia's Dispatcher pins itself to whichever thread first touches it (e.g. via
// constructing a Control that starts a DispatcherTimer, or a View whose XAML wires up an
// ItemsCollection). Running test classes in parallel worker threads makes that pinning
// racy: any Avalonia-touching test on a different thread than the one that first claimed
// the Dispatcher fails with "the calling thread cannot access this object because a
// different thread owns it". Disabling parallelization limits concurrent tests as defense
// in depth, but does not guarantee physical thread identity; AvaloniaUiThread provides that
// guarantee for tests that construct Avalonia objects.
[assembly: CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)]
