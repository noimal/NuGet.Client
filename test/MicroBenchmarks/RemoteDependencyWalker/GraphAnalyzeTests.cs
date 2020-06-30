// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NuGet.DependencyResolver;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Versioning;
using Test.Utility;
using Xunit;
using Xunit.Abstractions;

namespace NuGet.MicroBenchmarks.Tests
{
    public class GraphAnalyzeTests
    {
        private ITestOutputHelper OutputHelper { get; }

        public GraphAnalyzeTests(ITestOutputHelper outputHelper)
        {
            OutputHelper = outputHelper;
        }

        [Theory]
        [InlineData(0, 2, 10, 2)] // there wil be 2^11-1 = 2,047  nodes in the graph
        //[InlineData(100, 2, 10, 2)] // there wil be 2^11-1 = 2,047  nodes in the graph
        //[InlineData(100, 2, 10, 4)]
        //[InlineData(100, 2, 10, 6)]
        //[InlineData(100, 4, 6, 2)] // there wil be (4^7-1)/3 =  5,461 nodes in the graph
        //[InlineData(100, 4, 6, 4)]      
        public async Task WalkAsync_WithoutAmbiguousNodes(int iterations, int childNodeCount, int graphDepth, int depthOfCentralDependencies)
        {
            // warm - up
            var analyseGraphWithOneTransitiveDependencyMultipleParentsResult = await AnalyseGraphWithOneCentralTransitiveDependencyMultipleParents(childNodeCount, graphDepth, depthOfCentralDependencies);

            var analyseGraphWithoutTransitiveDepsAsyncResult = await AnalyseGraphWithoutCentralTransitiveDepsAsync(childNodeCount, graphDepth); ;
            var analyseGraphWithMultipleTransitiveDependenciesOneParentResult = await AnalyseGraphWithMultipleCentralTransitiveDependenciesOneParent(childNodeCount, graphDepth, depthOfCentralDependencies); ;

            // minimal validation
            Assert.Equal(childNodeCount, analyseGraphWithoutTransitiveDepsAsyncResult.root.InnerNodes.Count);
            Assert.Equal(childNodeCount + Math.Pow(childNodeCount, depthOfCentralDependencies + 1), analyseGraphWithMultipleTransitiveDependenciesOneParentResult.root.InnerNodes.Count);
            Assert.Equal(childNodeCount + 1, analyseGraphWithOneTransitiveDependencyMultipleParentsResult.root.InnerNodes.Count);

            var centralTransitiveDep = analyseGraphWithOneTransitiveDependencyMultipleParentsResult.root.InnerNodes.Where(n => n.Item.IsCentralTransitive).First();
            var parents = (List<GraphNode<RemoteResolveResult>>)centralTransitiveDep.GetType().GetProperty("ParentNodes", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(centralTransitiveDep);

            Assert.Equal(Math.Pow(childNodeCount, depthOfCentralDependencies), parents.Count);

            Execute(iterations,
                $"{nameof(WalkAsync_WithoutAmbiguousNodes)}_CC{childNodeCount}_GD{graphDepth}",
                async (csvWriter) =>
                {
                    analyseGraphWithoutTransitiveDepsAsyncResult = await AnalyseGraphWithoutCentralTransitiveDepsAsync(childNodeCount, graphDepth);
                    csvWriter.Write($"{childNodeCount}_{graphDepth}_{depthOfCentralDependencies}_AnalyseGraphWithoutCentralTransitiveDepsAsync", analyseGraphWithoutTransitiveDepsAsyncResult.executionElapsedMilliseconds);

                    analyseGraphWithMultipleTransitiveDependenciesOneParentResult = await AnalyseGraphWithMultipleCentralTransitiveDependenciesOneParent(childNodeCount, graphDepth, depthOfCentralDependencies);
                    csvWriter.Write($"{childNodeCount}_{graphDepth}_{depthOfCentralDependencies}_AnalyseGraphWith{Math.Pow(childNodeCount, depthOfCentralDependencies + 1)}CentralTransitiveDependenciesOneParent", analyseGraphWithMultipleTransitiveDependenciesOneParentResult.executionElapsedMilliseconds);

                    analyseGraphWithOneTransitiveDependencyMultipleParentsResult = await AnalyseGraphWithOneCentralTransitiveDependencyMultipleParents(childNodeCount, graphDepth, depthOfCentralDependencies);
                    csvWriter.Write($"{childNodeCount}_{graphDepth}_{depthOfCentralDependencies}_AnalyseGraphWithOneCentralTransitiveDependency{Math.Pow(childNodeCount, depthOfCentralDependencies)}Parents", analyseGraphWithOneTransitiveDependencyMultipleParentsResult.executionElapsedMilliseconds);
                });
        }

        [Theory]
        [InlineData(0, 2, 10, 2)]
        //[InlineData(100, 2, 10, 2)] // there wil be 2^11-1 = 2,047 in the graph. Half of the transitive will be ambigous and eventually will get rejected
        //[InlineData(100, 2, 10, 4)]
        //[InlineData(100, 2, 10, 6)]
        public async Task WalkAsync_WithAmbiguousNodes(int iterations, int childNodeCount, int graphDepth, int depthOfCentralDependencies)
        {
            // warm-up
            var analyseGraphWithMultipleRejectedCentralTransitiveDepsResult = await AnalyseGraphWithMultipleRejectedCentralTransitiveDepsAsync(childNodeCount, graphDepth, depthOfCentralDependencies);
            var analyseGraphWithoutCentralTransitiveDepsAsync_HalfGraphRejectedResult = await AnalyseGraphWithoutCentralTransitiveDepsAsync_HalfGraphRejected(childNodeCount, graphDepth);
            var analyseGraphWithMultipleAcceptedCentralTransitiveDepsResult = await AnalyseGraphWithMultipleAcceptedCentralTransitiveDepsAsync(childNodeCount, graphDepth, depthOfCentralDependencies);

            // minimal validation
            Assert.Equal(childNodeCount, analyseGraphWithoutCentralTransitiveDepsAsync_HalfGraphRejectedResult.root.InnerNodes.Count);
            var areAnyNotRejectedNodesInTheFirstHalfGraph = analyseGraphWithoutCentralTransitiveDepsAsync_HalfGraphRejectedResult
                        .root
                        .InnerNodes
                        .Take(childNodeCount / 2)
                        .SelectMany(n => n.InnerNodes)
                        .Any(n => n.Disposition != Disposition.Rejected);
            Assert.False(areAnyNotRejectedNodesInTheFirstHalfGraph);

            var acceptedCentralTransitiveDependencies = analyseGraphWithMultipleAcceptedCentralTransitiveDepsResult.root
                        .InnerNodes
                        .Where(n => n.Item.IsCentralTransitive && n.Disposition == Disposition.Accepted)
                        .ToList();
            var notAcceptedCentralTransitiveDependencies = analyseGraphWithMultipleAcceptedCentralTransitiveDepsResult.root
                        .InnerNodes
                        .Where(n => n.Item.IsCentralTransitive && n.Disposition != Disposition.Accepted)
                        .ToList();
            Assert.Equal(Math.Pow(childNodeCount, depthOfCentralDependencies), acceptedCentralTransitiveDependencies.Count);
            Assert.Equal(0, notAcceptedCentralTransitiveDependencies.Count);

            var rejectedCentralTransitiveDependencies = analyseGraphWithMultipleRejectedCentralTransitiveDepsResult.root
                        .InnerNodes
                        .Where(n => n.Item.IsCentralTransitive && n.Disposition == Disposition.Rejected)
                        .ToList();
            var notRejectedCentralTransitiveDependencies = analyseGraphWithMultipleRejectedCentralTransitiveDepsResult.root
                        .InnerNodes
                        .Where(n => n.Item.IsCentralTransitive && n.Disposition != Disposition.Rejected)
                        .ToList();
            Assert.Equal(Math.Pow(childNodeCount, depthOfCentralDependencies), rejectedCentralTransitiveDependencies.Count);
            Assert.Equal(0, notRejectedCentralTransitiveDependencies.Count);

            Execute(iterations,
                $"{nameof(WalkAsync_WithAmbiguousNodes)}_CC{childNodeCount}_GD{graphDepth}",
                async (csvWriter) =>
                {
                    analyseGraphWithoutCentralTransitiveDepsAsync_HalfGraphRejectedResult = await AnalyseGraphWithoutCentralTransitiveDepsAsync_HalfGraphRejected(childNodeCount, graphDepth);
                    csvWriter.Write($"{childNodeCount}_{graphDepth}_{depthOfCentralDependencies}_AnalyseGraphWithoutCentralTransitiveDepsAsync_HalfGraphRejected", analyseGraphWithoutCentralTransitiveDepsAsync_HalfGraphRejectedResult.executionElapsedMilliseconds);

                    analyseGraphWithMultipleAcceptedCentralTransitiveDepsResult = await AnalyseGraphWithMultipleAcceptedCentralTransitiveDepsAsync(childNodeCount, graphDepth, depthOfCentralDependencies);
                    csvWriter.Write($"{childNodeCount}_{graphDepth}_{depthOfCentralDependencies}_AnalyseGraphWith{Math.Pow(childNodeCount, depthOfCentralDependencies)}AcceptedCentralTransitiveDeps", analyseGraphWithMultipleAcceptedCentralTransitiveDepsResult.executionElapsedMilliseconds);

                    analyseGraphWithMultipleRejectedCentralTransitiveDepsResult = await AnalyseGraphWithMultipleRejectedCentralTransitiveDepsAsync(childNodeCount, graphDepth, depthOfCentralDependencies);
                    csvWriter.Write($"{childNodeCount}_{graphDepth}_{depthOfCentralDependencies}_AnalyseGraphWith{Math.Pow(childNodeCount, depthOfCentralDependencies)}RejectedCentralTransitiveDependencies", analyseGraphWithMultipleRejectedCentralTransitiveDepsResult.executionElapsedMilliseconds);
                });
        }

        /// <summary>
        /// The direct nodes will be split in two
        /// The first half will add a graph with one version 1.0.0
        /// The other half wil have the same Transitive packages but with higher version 2.0.0
        /// </summary>
        /// <param name="childNodeCount">ChildCount.</param>
        /// <param name="depth">The depth of the graph.</param>
        /// <returns></returns>
        private async Task<(long executionElapsedMilliseconds, AnalyzeResult<RemoteResolveResult> result, GraphNode<RemoteResolveResult> root)> AnalyseGraphWithoutCentralTransitiveDepsAsync_HalfGraphRejected(int childNodeCount, int depth)
        {
            var framework = NuGetFramework.Parse("net45");
            var version100 = "1.0.0";
            var version200 = "2.0.0";
            var context = new TestRemoteWalkContext();
            var provider = new DependencyProvider();
            var rootNoTransitiveDepencies = provider.Package("ProjectStart", version100);
            List<(string id, string version)> directNodes = new List<(string id, string version)>();
            int halfChildrenCount = childNodeCount / 2;

            for (int i = 1; i <= halfChildrenCount; i++)
            {
                directNodes.Add(($"FirstDirectHalf{i}", version100));
            }
            CreatePackageGraphFromDirectNodes(provider, rootNoTransitiveDepencies, directNodes, childNodeCount, depth, "TransitiveDep", version100);

            //add seconf half of the graph
            directNodes.Clear();
            for (int i = 1; i <= halfChildrenCount; i++)
            {
                directNodes.Add(($"SecondDirectHalf{i}", version100));
            }
            CreatePackageGraphFromDirectNodes(provider, rootNoTransitiveDepencies, directNodes, childNodeCount, depth, "TransitiveDep", version200);

            context.LocalLibraryProviders.Add(provider);
            var walker = new RemoteDependencyWalker(context);
            var rootNode = await DoWalkAsync(walker, "ProjectStart", framework);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            AnalyzeResult<RemoteResolveResult> result = rootNode.Analyze();
            sw.Stop();
            return (sw.ElapsedMilliseconds, result, rootNode);
        }

        /// <summary>
        /// The direct nodes will be split in two
        /// The first half will add a graph with one version 1.0.0
        /// The other half wil have the same Transitive packages but with higher version 2.0.0
        /// At a certain depth "depthOfCentralDependencies" the transitive dependencies become central
        /// The central transitive deps will have half of parents rejected and half of them accepted
        /// </summary>
        /// <returns></returns>
        private async Task<(long executionElapsedMilliseconds, AnalyzeResult<RemoteResolveResult> result, GraphNode<RemoteResolveResult> root)> AnalyseGraphWithMultipleAcceptedCentralTransitiveDepsAsync(int childNodeCount, int depth, int depthOfCentralDependencies)
        {
            var framework = NuGetFramework.Parse("net45");
            var version100 = "1.0.0";
            var version200 = "2.0.0";
            var context = new TestRemoteWalkContext();
            var provider = new DependencyProvider();
            var root = provider.Package("ProjectStart", version100);
            List<(string id, string version)> directNodes = new List<(string id, string version)>();
            for (int i = 1; i <= childNodeCount / 2; i++)
            {
                directNodes.Add(($"FirstDirectHalf{i}", version100));
            }

            //the transitive nodes are created with ids like $"{packageIdPrefix}_{currentDepth}x{currentChildCount}"
            CreatePackageGraphFromDirectNodes(provider, root, directNodes, childNodeCount, depth, "TransitiveDep", version100);

            // add seconf half
            directNodes.Clear();
            for (int i = 1; i <= childNodeCount / 2; i++)
            {
                directNodes.Add(($"SecondDirectHalf{i}", version200));
            }

            //the transitive nodes are created with ids like $"{packageIdPrefix}_{currentDepth}x{currentChildCount}"
            CreatePackageGraphFromDirectNodes(provider, root, directNodes, childNodeCount, depth, "TransitiveDep", version200);

            // add the transitive dependencies; in normal case it should be power of"depthOfCentralDependencies+1" but the direct dependencies were split in 2 so the pwer decreased to depthOfCentralDependencies
            // the central transitive dependencies will have two parents each - one is going to be rejected and one accepted
            var centralTransitiveDependenciesChildren = (int)Math.Pow(childNodeCount, depthOfCentralDependencies);
            AddCentralTransitivePackagesToProject(root,
                Enumerable.Range(0, centralTransitiveDependenciesChildren)
                .Select(i => ($"TransitiveDep_{depthOfCentralDependencies}x{i}", version200)).ToList());

            context.LocalLibraryProviders.Add(provider);
            var walker = new RemoteDependencyWalker(context);

            // the input graph for a test with child count = 2 and the depth 3 
            //                                      root
            //                             /                     \
            //                         D1                            D2
            //                    /         \                    /         \
            //               T11(v1)       T12(v1)          T11(v2)       T12(v2)
            //                / \         /     \            /  \          /      \
            //         T21(v1)  T22(v1) T23(v1) T24(v1) T21(v2) T22(v2) T23(v2) T24(v2)   <======= all these will be central transitive with version v2

            // the below calculated graph will be
            //                                                              root
            //                               /                          /         \           \        \      \
            //                             /                           /           \           \        \      \
            //                         D1                            D2              T21(v2)  T22(v2) T23(v2) T24(v2)
            //                    /         \                    /         \
            //               T11(v1)       T12(v1)          T11(v2)       T12(v2)
            var rootNode = await DoWalkAsync(walker, "ProjectStart", framework);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // After the Ananlyze step the graph will be ; the ** marked nodes wil be rejected 
            //                                                              root
            //                               /                          /         \           \        \      \
            //                             /                           /           \           \        \      \
            //                         D1                           D2              T21(v2)  T22(v2) T23(v2) T24(v2)
            //                    /         \                    /         \
            //               **T11(v1)       **T12(v1)          T11(v2)       T12(v2)
            AnalyzeResult<RemoteResolveResult> result = rootNode.Analyze();
            sw.Stop();
            return (sw.ElapsedMilliseconds, result, rootNode);
        }

        /// <summary>
        /// The direct nodes will be split in two
        /// The first half will add a graph with one version 1.0.0
        /// The other half wil have the same Transitive packages but with higher version 2.0.0
        /// At a certain depth "depthOfCentralDependencies" the transitive dependencies of the subtree of version 1 become central
        /// The central transitive deps will become rejected as the parents will be rejected
        /// </summary>
        /// <returns></returns>
        private async Task<(long executionElapsedMilliseconds, AnalyzeResult<RemoteResolveResult> result, GraphNode<RemoteResolveResult> root)> AnalyseGraphWithMultipleRejectedCentralTransitiveDepsAsync(int childNodeCount, int depth, int depthOfCentralDependencies)
        {
            var framework = NuGetFramework.Parse("net45");
            var version100 = "1.0.0";
            var version200 = "2.0.0";
            var context = new TestRemoteWalkContext();
            var provider = new DependencyProvider();
            var root = provider.Package("ProjectStart", version100);
            List<(string id, string version)> directNodes = new List<(string id, string version)>();
            for (int i = 1; i <= childNodeCount / 2; i++)
            {
                directNodes.Add(($"FirstDirectHalf{i}", version100));
            }

            //the transitive nodes are created with ids like $"{packageIdPrefix}_{currentDepth}x{currentChildCount}"
            CreatePackageGraphFromDirectNodesWithTransitiveIdChanged(provider,
                root,
                directNodes,
                childNodeCount,
                depth,
                "TransitiveDep",
                version100,
                changedTransitivePackageIdPrefix: "CentralTransitiveDep",
                changedFromDepth: depthOfCentralDependencies);

            // add seconf half
            directNodes.Clear();
            for (int i = 1; i <= childNodeCount / 2; i++)
            {
                directNodes.Add(($"SecondDirectHalf{i}", version200));
            }

            //the transitive nodes are created with ids like $"{packageIdPrefix}_{currentDepth}x{currentChildCount}"
            CreatePackageGraphFromDirectNodes(provider, root, directNodes, childNodeCount, depth, "TransitiveDep", version200);

            // add the transitive dependencies; in normal case it should be power of"depthOfCentralDependencies+1" but the direct dependencies were split in 2 so the power decreased to depthOfCentralDependencies
            // the central transitive dependencies will have two parents each - one is gong to be rejected and one accepted
            var centralTransitiveDependenciesChildren = (int)Math.Pow(childNodeCount, depthOfCentralDependencies);
            AddCentralTransitivePackagesToProject(root,
                Enumerable.Range(0, centralTransitiveDependenciesChildren)
                .Select(i => ($"CentralTransitiveDep_{depthOfCentralDependencies}x{i}", version100)).ToList());

            context.LocalLibraryProviders.Add(provider);
            var walker = new RemoteDependencyWalker(context);

            // the input graph for a test with child count = 2 and the depth 3 
            //                                      root
            //                             /                     \
            //                         D1                            D2
            //                    /         \                    /         \
            //               T11(v1)       T12(v1)          T11(v2)       T12(v2)
            //                / \         /     \            /  \          /      \
            //         CT21(v1)  CT22(v1) CT23(v1) CT24(v1) T21(v2) T22(v2) T23(v2) T24(v2)   <======= all the CT** will be central transitive with version v2

            // the below calculated graph will be
            //                                                              root
            //                               /                          /         \           \        \      \
            //                             /                           /           \           \        \      \
            //                         D1                            D2              CT21(v2)  CT22(v2) CT23(v2) CT24(v2)
            //                    /         \                    /         \
            //               T11(v1)       T12(v1)          T11(v2)       T12(v2)
            //                                                /  \          /      \
            //                                            T21(v2) T22(v2) T23(v2) T24(v2)
            var rootNode = await DoWalkAsync(walker, "ProjectStart", framework);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // After the Ananlyze step the graph will be ; the ** marked nodes wil be rejected 
            //                                                              root
            //                               /                          /         \           \        \      \
            //                             /                           /           \           \        \      \
            //                         D1                            D2              **CT21(v2)  **CT22(v2) **CT23(v2) **CT24(v2)
            //                    /         \                    /         \
            //               **T11(v1)      **T12(v1)          T11(v2)       T12(v2)
            //                                                /  \          /      \
            //                                            T21(v2) T22(v2) T23(v2) T24(v2)
            AnalyzeResult<RemoteResolveResult> result = rootNode.Analyze();
            sw.Stop();
            return (sw.ElapsedMilliseconds, result, rootNode);
        }

        private async Task<(long executionElapsedMilliseconds, AnalyzeResult<RemoteResolveResult> result, GraphNode<RemoteResolveResult> root)> AnalyseGraphWithoutCentralTransitiveDepsAsync(int childNodeCount, int depth)
        {
            var framework = NuGetFramework.Parse("net45");
            var version100 = "1.0.0";
            var context = new TestRemoteWalkContext();
            var provider = new DependencyProvider();
            var rootNoTransitiveDepencies = provider.Package("ProjectStart", version100);
            List<(string id, string version)> directNodes = new List<(string id, string version)>();
            for (int i = 1; i <= childNodeCount; i++)
            {
                directNodes.Add(($"Direct{i}", version100));
            }
            CreatePackageGraphFromDirectNodes(provider, rootNoTransitiveDepencies, directNodes, childNodeCount, depth, "TransitiveDep", version100);

            context.LocalLibraryProviders.Add(provider);
            var walker = new RemoteDependencyWalker(context);
            var rootNode = await DoWalkAsync(walker, "ProjectStart", framework);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            AnalyzeResult<RemoteResolveResult> result = rootNode.Analyze();
            sw.Stop();
            return (sw.ElapsedMilliseconds, result, rootNode);
        }

        /// <summary>
        /// The transitive dependencies at a specific level will be central
        /// Each central dependency will have one parent
        /// </summary>
        private async Task<(long executionElapsedMilliseconds, AnalyzeResult<RemoteResolveResult> result, GraphNode<RemoteResolveResult> root)> AnalyseGraphWithMultipleCentralTransitiveDependenciesOneParent(int childNodeCount, int depth, int depthOfCentralDependencies)
        {
            var framework = NuGetFramework.Parse("net45");
            var version100 = "1.0.0";
            var context = new TestRemoteWalkContext();
            var provider = new DependencyProvider();
            var root = provider.Package("ProjectStart", version100);
            List<(string id, string version)> directNodes = new List<(string id, string version)>();
            for (int i = 1; i <= childNodeCount; i++)
            {
                directNodes.Add(($"Direct{i}", version100));
            }

            //the transitive nodes are created with ids like $"{packageIdPrefix}_{currentDepth}x{currentChildCount}"
            CreatePackageGraphFromDirectNodes(provider, root, directNodes, childNodeCount, depth, "TransitiveDep", version100);
            // add the transitive dependencies; because the first level is for direct dependencies use depthOfCentralDependencies + 1 
            var centralTransitiveDependenciesChildren = (int)Math.Pow(childNodeCount, depthOfCentralDependencies + 1);
            AddCentralTransitivePackagesToProject(root,
                Enumerable.Range(0, centralTransitiveDependenciesChildren)
                .Select(i => ($"TransitiveDep_{depthOfCentralDependencies}x{i}", version100)).ToList());

            context.LocalLibraryProviders.Add(provider);
            var walker = new RemoteDependencyWalker(context);
            var rootNode = await DoWalkAsync(walker, "ProjectStart", framework);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            AnalyzeResult<RemoteResolveResult> result = rootNode.Analyze();
            sw.Stop();
            return (sw.ElapsedMilliseconds, result, rootNode);
        }

        /// <summary>
        /// The transitive dependencies at a specific level will have one common central dependency 
        /// The central transitive depencency will have a set of childNodeCount^depthOfCentralDependencies parents
        /// </summary>
        private async Task<(long executionElapsedMilliseconds, AnalyzeResult<RemoteResolveResult> result, GraphNode<RemoteResolveResult> root)> AnalyseGraphWithOneCentralTransitiveDependencyMultipleParents(
            int childNodeCount,
            int depth,
            int depthOfCentralDependencies)
        {
            var framework = NuGetFramework.Parse("net45");
            var version100 = "1.0.0";
            var context = new TestRemoteWalkContext();
            var provider = new DependencyProvider();
            var root = provider.Package("ProjectStart", version100);
            List<(string id, string version)> directNodes = new List<(string id, string version)>();
            for (int i = 1; i <= childNodeCount; i++)
            {
                directNodes.Add(($"Direct{i}", version100));
            }

            //the transitive nodes are created with ids like $"{packageIdPrefix}_{currentDepth}x{currentChildCount}"
            CreatePackageGraphFromDirectNodes(provider,
                root,
                directNodes,
                childNodeCount,
                depth,
                "TransitiveDep",
                version100,
                "CentralTransitiveDep",
                version100, depthOfCentralDependencies);

            AddCentralTransitivePackagesToProject(root, new List<(string id, string version)>() { ("CentralTransitiveDep", version100) });

            context.LocalLibraryProviders.Add(provider);
            var walker = new RemoteDependencyWalker(context);
            var rootNode = await DoWalkAsync(walker, "ProjectStart", framework);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            AnalyzeResult<RemoteResolveResult> result = rootNode.Analyze();
            sw.Stop();
            return (sw.ElapsedMilliseconds, result, rootNode);
        }

        private void CreatePackageGraphFromDirectNodes(DependencyProvider provider,
            DependencyProvider.TestPackage root,
            List<(string id, string version)> directNodes,
            int childNodeCount,
            int treeDepth,
            string transitivePackageIdPrefix,
            string transitivePackageVersion)
        {
            int index = 0;
            foreach (var directNode in directNodes)
            {
                var directDep = provider.Package(directNode.id, directNode.version);
                root.DependsOn(directNode.id, directNode.version);
                CreatePackageGraph(
                    provider,
                    directDep,
                    childNodeCount: childNodeCount,
                    currentDepth: 1,
                    currentBreath: index,
                    treeDepth: treeDepth - 1,
                    transitivePackageIdPrefix,
                    transitivePackageVersion);
                index++;
            }
        }

        private void CreatePackageGraphFromDirectNodesWithTransitiveIdChanged(DependencyProvider provider,
           DependencyProvider.TestPackage root,
           List<(string id, string version)> directNodes,
           int childNodeCount,
           int treeDepth,
           string transitivePackageIdPrefix,
           string transitivePackageVersion,
           string changedTransitivePackageIdPrefix,
           int changedFromDepth)
        {
            int index = 0;
            foreach (var directNode in directNodes)
            {
                var directDep = provider.Package(directNode.id, directNode.version);
                root.DependsOn(directNode.id, directNode.version);
                CreatePackageGraphWithTransitiveIdChanged(
                    provider,
                    directDep,
                    childNodeCount: childNodeCount,
                    currentDepth: 1,
                    currentBreath: index,
                    treeDepth: treeDepth - 1,
                    transitivePackageIdPrefix,
                    transitivePackageVersion,
                    changedTransitivePackageIdPrefix,
                    changedFromDepth);
                index++;
            }
        }

        private void CreatePackageGraphFromDirectNodes(DependencyProvider provider,
            DependencyProvider.TestPackage root,
            List<(string id, string version)> directNodes,
            int childNodeCount,
            int treeDepth,
            string transitivePackageIdPrefix,
            string transitivePackageVersion,
            string enforcedTransitivePackageId,
            string enforcedTransitivePackageVersion,
            int enforcedTransitivePackageDepth
            )
        {
            int index = 0;
            foreach (var directNode in directNodes)
            {
                var directDep = provider.Package(directNode.id, directNode.version);

                root.DependsOn(directNode.id, directNode.version);
                CreatePackageGraph(
                                   provider,
                                   directDep,
                                   childNodeCount: childNodeCount,
                                   currentDepth: 1,
                                   currentBreath: index,
                                   treeDepth: treeDepth - 1,
                                   transitivePackageIdPrefix,
                                   transitivePackageVersion,
                                   enforcedTransitivePackageId,
                                   enforcedTransitivePackageVersion,
                                   enforcedTransitivePackageDepth);

                index++;
            }
        }

        private void CreatePackageGraph(
            DependencyProvider provider,
            DependencyProvider.TestPackage root,
            int childNodeCount,
            int currentDepth,
            int currentBreath,
            int treeDepth,
            string packageIdPrefix,
            string packageVersion)
        {
            if (currentDepth > treeDepth)
            {
                return;
            }
            for (int i = 0; i < childNodeCount; i++)
            {
                var nodeBreath = currentBreath * childNodeCount + i;
                var package = provider.Package($"{packageIdPrefix}_{currentDepth}x{nodeBreath}", $"{packageVersion}");
                root.DependsOn($"{packageIdPrefix}_{currentDepth}x{nodeBreath}", $"{packageVersion}");
                CreatePackageGraph(provider,
                    package,
                    childNodeCount,
                    currentDepth: currentDepth + 1,
                    currentBreath: nodeBreath,
                    treeDepth: treeDepth,
                    packageIdPrefix,
                    packageVersion);
            }
        }

        private void CreatePackageGraphWithTransitiveIdChanged(
           DependencyProvider provider,
           DependencyProvider.TestPackage root,
           int childNodeCount,
           int currentDepth,
           int currentBreath,
           int treeDepth,
           string packageIdPrefix,
           string packageVersion,
           string changedTransitivePackageIdPrefix,
           int changedFromDepth)
        {
            if (currentDepth > treeDepth)
            {
                return;
            }
            for (int i = 0; i < childNodeCount; i++)
            {
                var nodeBreath = currentBreath * childNodeCount + i;
                var packageId = currentDepth >= changedFromDepth ?
                    $"{changedTransitivePackageIdPrefix}_{currentDepth}x{nodeBreath}" :
                    $"{packageIdPrefix}_{currentDepth}x{nodeBreath}";

                var package = provider.Package(packageId, $"{packageVersion}");
                root.DependsOn(packageId, $"{packageVersion}");
                CreatePackageGraphWithTransitiveIdChanged(provider,
                    package,
                    childNodeCount,
                    currentDepth: currentDepth + 1,
                    currentBreath: nodeBreath,
                    treeDepth: treeDepth,
                    packageIdPrefix,
                    packageVersion,
                    changedTransitivePackageIdPrefix,
                    changedFromDepth);
            }
        }

        private void CreatePackageGraph(
            DependencyProvider provider,
            DependencyProvider.TestPackage root,
            int childNodeCount,
            int currentDepth,
            int currentBreath,
            int treeDepth,
            string packageIdPrefix,
            string packageVersion,
            string enforcedTransitivePackageId,
            string enforcedTransitivePackageVersion,
            int enforcedTransitivePackageDepth)
        {
            if (currentDepth > treeDepth)
            {
                return;
            }
            bool depthContainsEnforcedTransitivePackage = enforcedTransitivePackageDepth == currentDepth;
            int iterationCount = depthContainsEnforcedTransitivePackage ? childNodeCount - 1 : childNodeCount;
            for (int i = 0; i < iterationCount; i++)
            {
                var nodeBreath = currentBreath * childNodeCount + i;

                var package = provider.Package($"{packageIdPrefix}_{currentDepth}x{nodeBreath}", $"{packageVersion}");
                root.DependsOn($"{packageIdPrefix}_{currentDepth}x{nodeBreath}", $"{packageVersion}");
                CreatePackageGraph(provider,
                    package,
                    childNodeCount: childNodeCount,
                    currentDepth: currentDepth + 1,
                    currentBreath: nodeBreath,
                    treeDepth: treeDepth,
                    packageIdPrefix,
                    packageVersion,
                    enforcedTransitivePackageId,
                    enforcedTransitivePackageVersion,
                    enforcedTransitivePackageDepth);
            }
            if (depthContainsEnforcedTransitivePackage)
            {
                var package = provider.Package(enforcedTransitivePackageId, enforcedTransitivePackageVersion);
                root.DependsOn(enforcedTransitivePackageId, enforcedTransitivePackageVersion);
                // do not create more children that needed
                if (package.DependenciesCount < childNodeCount)
                {
                    CreatePackageGraph(provider,
                        package,
                        childNodeCount: childNodeCount,
                        currentDepth: currentDepth + 1,
                        currentBreath: currentBreath * childNodeCount + childNodeCount - 1,
                        treeDepth: treeDepth,
                        packageIdPrefix,
                        packageVersion);
                }
            }
        }

        //private void CreateCentralTransitivePackage(
        //    DependencyProvider provider,
        //    int childNodeCount,
        //    int treeDepth,
        //    string packageIdPrefix,
        //    string packageVersion,
        //    string enforcedTransitivePackageId,
        //    string enforcedTransitivePackageVersion,
        //    int enforcedTransitivePackageDepth)
        //{
        //    var package = provider.Package(enforcedTransitivePackageId, enforcedTransitivePackageVersion);
        //    CreatePackageGraph(provider,
        //        package,
        //        childNodeCount: childNodeCount,
        //        currentDepth: enforcedTransitivePackageDepth + 1,
        //        currentBreath: 0,
        //        treeDepth: treeDepth,
        //        enforcedTransitivePackageId,
        //        enforcedTransitivePackageVersion);
        //}

        private void AddCentralTransitivePackagesToProject(DependencyProvider.TestPackage project, List<(string id, string version)> centralTransitivePackages)
        {
            foreach (var p in centralTransitivePackages)
            {
                project.DependsOn(p.id, p.version, LibraryDependencyTarget.Package, versionCentrallyManaged: true, libraryDependencyReferenceType: LibraryDependencyReferenceType.None);
            }
        }

        private Task<GraphNode<RemoteResolveResult>> DoWalkAsync(RemoteDependencyWalker walker, string name)
        {
            return DoWalkAsync(walker, name, NuGetFramework.Parse("net45"));
        }

        private Task<GraphNode<RemoteResolveResult>> DoWalkAsync(RemoteDependencyWalker walker, string name, NuGetFramework framework)
        {
            var range = new LibraryRange
            {
                Name = name,
                VersionRange = new VersionRange(new NuGetVersion("1.0"))
            };

            return walker.WalkAsync(range, framework, runtimeIdentifier: null, runtimeGraph: null, recursive: true);

        }

        private void Execute(int iterations, string scenario, Action<CSVWriter> action)
        {
            if (iterations <= 0)
            {
                return;
            }

            using (var csvWriter = new CSVWriter(scenario))
            {
                for (int i = 0; i < iterations; i++)
                {
                    action(csvWriter);
                }
            }
        }
    }
}