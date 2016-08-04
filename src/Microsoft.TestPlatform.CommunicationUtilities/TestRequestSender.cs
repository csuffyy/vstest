// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.VisualStudio.TestPlatform.CommunicationUtilities
{
    using System;
    using ObjectModel;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using Microsoft.VisualStudio.TestPlatform.ObjectModel;
    using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;

    using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.Interfaces;

    /// <summary>
    /// Utility class that facilitates the IPC comunication. Acts as server.
    /// </summary>
    public sealed class TestRequestSender : ITestRequestSender
    {
        private ICommunicationManager communicationManager;

        private IDataSerializer dataSerializer;

        public TestRequestSender() : this(new SocketCommunicationManager(), JsonDataSerializer.Instance)
        {
        }

        internal TestRequestSender(ICommunicationManager communicationManager, IDataSerializer dataSerializer)
        {
            this.communicationManager = communicationManager;
            this.dataSerializer = dataSerializer;
        }

        /// <summary>
        /// Creates an endpoint and listens for client connection asynchronously
        /// </summary>
        /// <returns></returns>
        public int InitializeCommunication()
        {
            var port = this.communicationManager.HostServer();
            this.communicationManager.AcceptClientAsync();
            return port;
        }

        public bool WaitForRequestHandlerConnection(int clientConnectionTimeout)
        {
            return this.communicationManager.WaitForClientConnection(clientConnectionTimeout);
        }

        public void Dispose()
        {
            this.communicationManager?.StopServer();
        }

        /// <summary>
        /// Closes the connection
        /// </summary>
        public void Close()
        {
            this.Dispose();
            EqtTrace.Info("Closing the connection");
        }

        /// <summary>
        /// Initializes the extensions in the test host.
        /// </summary>
        /// <param name="pathToAdditionalExtensions">Path to additional extensions.</param>
        /// <param name="loadOnlyWellKnownExtensions">Flag to indicate if only well known extensions are to be loaded.</param>
        public void InitializeDiscovery(IEnumerable<string> pathToAdditionalExtensions, bool loadOnlyWellKnownExtensions)
        {
            this.communicationManager.SendMessage(MessageType.DiscoveryInitialize, pathToAdditionalExtensions);
        }

        /// <summary>
        /// Initializes the extensions in the test host.
        /// </summary>
        /// <param name="pathToAdditionalExtensions">Path to additional extensions.</param>
        /// <param name="loadOnlyWellKnownExtensions">Flag to indicate if only well known extensions are to be loaded.</param>
        public void InitializeExecution(IEnumerable<string> pathToAdditionalExtensions, bool loadOnlyWellKnownExtensions)
        {
            this.communicationManager.SendMessage(MessageType.ExecutionInitialize, pathToAdditionalExtensions);
        }

        /// <summary>
        /// Discovers tests in the sources passed with the criteria specifief.
        /// </summary>
        /// <param name="discoveryCriteria"> The criteria for discovery. </param>
        /// <param name="eventHandler"> The handler for discovery events from the test host. </param>
        public void DiscoverTests(DiscoveryCriteria discoveryCriteria, ITestDiscoveryEventsHandler discoveryEventsHandler)
        {
            this.communicationManager.SendMessage(MessageType.StartDiscovery, discoveryCriteria);

            var isDiscoveryComplete = false;

            // Cycle through the messages that the testhost sends. 
            // Currently each of the operations are not separate tasks since they should not each take much time. This is just a notification.
            while (!isDiscoveryComplete)
            {
                var rawMessage = this.communicationManager.ReceiveRawMessage();

                // Send raw message first to unblock handlers waiting to send message to IDEs
                discoveryEventsHandler.HandleRawMessage(rawMessage);

                var message = this.dataSerializer.DeserializeMessage(rawMessage);
                if (string.Equals(MessageType.TestCasesFound, message.MessageType))
                {
                    var testCases = this.dataSerializer.DeserializePayload<IEnumerable<TestCase>>(message);
                    discoveryEventsHandler.HandleDiscoveredTests(testCases);
                }
                else if (string.Equals(MessageType.DiscoveryComplete, message.MessageType))
                {
                    var discoveryCompletePayload = this.dataSerializer.DeserializePayload<DiscoveryCompletePayload>(message);
                    discoveryEventsHandler.HandleDiscoveryComplete(discoveryCompletePayload.TotalTests, discoveryCompletePayload.LastDiscoveredTests, discoveryCompletePayload.IsAborted);
                    isDiscoveryComplete = true;
                }
                else if (string.Equals(MessageType.TestMessage, message.MessageType))
                {
                    var testMessagePayload = this.dataSerializer.DeserializePayload<TestMessagePayload>(message);
                    discoveryEventsHandler.HandleLogMessage(testMessagePayload.MessageLevel, testMessagePayload.Message);
                }
            }
        }

        /// <summary>
        /// Ends the session with the test host.
        /// </summary>
        public void EndSession()
        {
            this.communicationManager.SendMessage(MessageType.SessionEnd);
        }

        /// <summary>
        /// Executes tests on the sources specified with the criteria mentioned.
        /// </summary>
        /// <param name="runCriteria">The test run criteria.</param>
        /// <param name="eventHandler">The handler for execution events from the test host.</param>
        public void StartTestRun(TestRunCriteriaWithSources runCriteria, ITestRunEventsHandler eventHandler)
        {
            this.communicationManager.SendMessage(MessageType.StartTestExecutionWithSources, runCriteria);

            // This needs to happen asynchronously.
            Task.Run(() => this.ListenAndReportTestResults(eventHandler));
        }

        /// <summary>
        /// Executes the specified tests with the criteria mentioned.
        /// </summary>
        /// <param name="runCriteria">The test run criteria.</param>
        /// <param name="eventHandler">The handler for execution events from the test host.</param>
        public void StartTestRun(TestRunCriteriaWithTests runCriteria, ITestRunEventsHandler eventHandler)
        {
            this.communicationManager.SendMessage(MessageType.StartTestExecutionWithTests, runCriteria);

            // This needs to happen asynchronously.
            Task.Run(() => this.ListenAndReportTestResults(eventHandler));
        }

        private void ListenAndReportTestResults(ITestRunEventsHandler testRunEventsHandler)
        {
            var isTestRunComplete = false;

            // Cycle through the messages that the testhost sends. 
            // Currently each of the operations are not separate tasks since they should not each take much time. This is just a notification.
            while (!isTestRunComplete)
            {
                var rawMessage = this.communicationManager.ReceiveRawMessage();

                // Send raw message first to unblock handlers waiting to send message to IDEs
                testRunEventsHandler.HandleRawMessage(rawMessage);

                var message = this.dataSerializer.DeserializeMessage(rawMessage);
                try
                {
                    if (string.Equals(MessageType.TestRunStatsChange, message.MessageType))
                    {
                        var testRunChangedArgs = dataSerializer.DeserializePayload<TestRunChangedEventArgs>(message);
                        testRunEventsHandler.HandleTestRunStatsChange(testRunChangedArgs);
                    }
                    else if (string.Equals(MessageType.ExecutionComplete, message.MessageType))
                    {
                        var testRunCompletePayload = dataSerializer.DeserializePayload<TestRunCompletePayload>(message);

                        testRunEventsHandler.HandleTestRunComplete(
                            testRunCompletePayload.TestRunCompleteArgs,
                            testRunCompletePayload.LastRunTests,
                            testRunCompletePayload.RunAttachments,
                            testRunCompletePayload.ExecutorUris);
                        isTestRunComplete = true;
                    }
                    else if (string.Equals(MessageType.TestMessage, message.MessageType))
                    {
                        var testMessagePayload = this.dataSerializer.DeserializePayload<TestMessagePayload>(message);
                        testRunEventsHandler.HandleLogMessage(testMessagePayload.MessageLevel, testMessagePayload.Message);
                    }
                    else if (string.Equals(MessageType.LaunchAdapterProcessWithDebuggerAttached, message.MessageType))
                    {
                        var testProcessStartInfo = this.dataSerializer.DeserializePayload<TestProcessStartInfo>(message);
                        int processId = testRunEventsHandler.LaunchProcessWithDebuggerAttached(testProcessStartInfo);

                        this.communicationManager.SendMessage(
                            MessageType.LaunchAdapterProcessWithDebuggerAttachedCallback, processId);
                    }
                }
                catch (Exception exception)
                {
                    EqtTrace.Error("Server: TestExecution: Message Deserialization failed with {0}", exception);
                    // notify of a test run complete and bail out.
                    testRunEventsHandler.HandleTestRunComplete(null, null, null, null);
                    isTestRunComplete = true;
                }
            }
        }
        
        /// <summary>
        /// Send the cancel message to test host
        /// </summary>
        public void SendTestRunCancel()
        {
            this.communicationManager.SendMessage(MessageType.CancelTestRun);
        }

        public void SendTestRunAbort()
        {
            this.communicationManager.SendMessage(MessageType.AbortTestRun);
        }
    }
}