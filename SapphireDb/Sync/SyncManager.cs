﻿using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using SapphireDb.Command.Subscribe;
using SapphireDb.Connection;
using SapphireDb.Internal;
using SapphireDb.Sync.Models;

namespace SapphireDb.Sync
{
    public class SyncManager
    {
        private readonly ILogger<SyncManager> logger;
        private readonly DbContextTypeContainer contextTypeContainer;
        private readonly SyncContext syncContext;
        private readonly ISapphireSyncModule sapphireSyncModule;

        public SyncManager(IServiceProvider serviceProvider, ILogger<SyncManager> logger, DbContextTypeContainer contextTypeContainer, SyncContext syncContext)
        {
            this.logger = logger;
            this.contextTypeContainer = contextTypeContainer;
            this.syncContext = syncContext;
            sapphireSyncModule = (ISapphireSyncModule) serviceProvider.GetService(typeof(ISapphireSyncModule));

            if (sapphireSyncModule != null)
            {
                sapphireSyncModule.SyncRequestRequestReceived += request =>
                {
                    if (request.OriginId == syncContext.SessionId)
                    {
                        return;
                    }

                    if (request.Propagate)
                    {
                        Publish(request);
                    }

                    if (request is SendChangesRequest sendChangesRequest)
                    {
                        Type dbType = contextTypeContainer.GetContext(sendChangesRequest.DbName);

                        if (dbType != null)
                        {
                            SapphireChangeNotifier changeNotifier =
                                (SapphireChangeNotifier) serviceProvider.GetService(typeof(SapphireChangeNotifier));
                            logger.LogInformation("Handling changes from other server");
                            logger.LogDebug(
                                "Handling {changeCount} changes of '{dbType}' from server with OriginId '{originId}'. Propagate: {propagate}",
                                sendChangesRequest.Changes.Count, dbType.Name,
                                sendChangesRequest.OriginId, sendChangesRequest.Propagate);
                            
                            changeNotifier.HandleChanges(sendChangesRequest.Changes, dbType);
                        }
                    }
                    else if (request is SendMessageRequest sendMessageRequest)
                    {
                        SapphireMessageSender sender =
                            (SapphireMessageSender) serviceProvider.GetService(typeof(SapphireMessageSender));
                        logger.LogInformation("Handling message from other server");
                        logger.LogDebug(
                            "Handling message for filter '{filter}' from server with OriginId '{originId}'. Propagate: {propagate}",
                            sendMessageRequest.Filter, sendMessageRequest.OriginId, sendMessageRequest.Propagate);
                        
                        sender.Send(sendMessageRequest.Message, sendMessageRequest.Filter,
                            sendMessageRequest.FilterParameters, false);
                    }
                    else if (request is SendPublishRequest sendPublishRequest)
                    {
                        SapphireMessageSender sender =
                            (SapphireMessageSender) serviceProvider.GetService(typeof(SapphireMessageSender));
                        logger.LogInformation("Handling publish from other server");
                        logger.LogDebug(
                            "Handling publish to topic '{topic}' from server with OriginId '{originId}'. Retain: {retain}, Propagate: {propagate}",
                            sendPublishRequest.Topic, sendPublishRequest.OriginId, sendPublishRequest.Retain, sendPublishRequest.Propagate);
                        
                        sender.Publish(sendPublishRequest.Topic, sendPublishRequest.Message, sendPublishRequest.Retain,
                            false);
                    }
                };
            }
        }

        public void SendChanges(List<ChangeResponse> changes, Type dbContextType)
        {
            SendChangesRequest sendChangesRequest = new SendChangesRequest()
            {
                Changes = changes,
                DbName = contextTypeContainer.GetName(dbContextType),
                OriginId = syncContext.SessionId
            };

            Publish(sendChangesRequest);
        }

        public void SendPublish(string topic, object message, bool retain)
        {
            SendPublishRequest sendPublishRequest = new SendPublishRequest()
            {
                Topic = topic,
                Message = message,
                Retain = retain,
                OriginId = syncContext.SessionId
            };

            Publish(sendPublishRequest);
        }

        public void SendMessage(object message, string filter, object[] filterParameters)
        {
            SendMessageRequest sendMessageRequest = new SendMessageRequest()
            {
                Message = message,
                Filter = filter,
                FilterParameters = filterParameters,
                OriginId = syncContext.SessionId
            };

            Publish(sendMessageRequest);
        }

        private void Publish(SyncRequest syncRequest)
        {
            if (sapphireSyncModule != null)
            {
                logger.LogInformation("Publishing sync request to other servers");
                sapphireSyncModule.Publish(syncRequest);
            }
        }
    }
}