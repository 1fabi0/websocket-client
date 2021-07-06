using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Websocket.Client.Logging;

namespace Websocket.Client
{
    public partial class WebsocketClient
    {
        private int _receivingBuffersSize;
        private List<ResponseMessage> _binaryResponseMessages;

        private async Task Listen(WebSocket client, CancellationToken token)
        {
            Exception causedException = null;
            try
            {
                // define buffer here and reuse, to avoid more allocation
                const int chunkSize = 1024 * 4;
                var buffer = new ArraySegment<byte>(new byte[chunkSize]);
                var resultBuffer = new byte[_receivingBuffersSize];
                var currentIndexResponseBuffer = 0;
                do
                {
                    WebSocketReceiveResult result;
                    var resultArraySize = 0;
                    bool isFirstChunk = true;

                    while (true)
                    {
                        result = await client.ReceiveAsync(buffer, token);
                        var currentChunk = buffer.Array;
                        var currentChunkSize = result.Count;

                        if (isFirstChunk)
                        {
                            // first chunk, use buffer as reference, do not allocate anything
                            Array.Copy(currentChunk, buffer.Offset, resultBuffer, 0, currentChunkSize);
                            resultArraySize += currentChunkSize;
                            isFirstChunk = false;
                        }
                        else if (currentChunk == null)
                        {
                            // weird chunk, do nothing
                        }
                        else
                        {
                            // insert current chunk
                            Array.Copy(currentChunk, buffer.Offset, resultBuffer, resultArraySize, currentChunkSize);
                            resultArraySize += currentChunkSize;
                        }

                        if (result.EndOfMessage)
                        {
                            break;
                        }
                    }

                    ResponseMessage message;
                    if (result.MessageType == WebSocketMessageType.Text && IsTextMessageConversionEnabled)
                    {
                        var data = resultArraySize != 0 ?
                            GetEncoding().GetString(resultBuffer.Take(resultArraySize).ToArray()) :
                                null;

                        message = ResponseMessage.TextMessage(data);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Logger.Trace(L($"Received close message"));

                        if (!IsStarted || _stopping)
                        {
                            return;
                        }

                        var info = DisconnectionInfo.Create(DisconnectionType.ByServer, client, null);
                        _disconnectedSubject.OnNext(info);

                        if (info.CancelClosing)
                        {
                            // closing canceled, reconnect if enabled
                            if (IsReconnectionEnabled)
                            {
                                throw new OperationCanceledException("Websocket connection was closed by server");
                            }

                            continue;
                        }

                        await StopInternal(client, WebSocketCloseStatus.NormalClosure, "Closing",
                            token, false, true);

                        // reconnect if enabled
                        if (IsReconnectionEnabled && !ShouldIgnoreReconnection(client))
                        {
                            _ = ReconnectSynchronized(ReconnectionType.Lost, false, null);
                        }

                        return;
                    }
                    else
                    {
                        message = _binaryResponseMessages[currentIndexResponseBuffer];
                        message.CopyInto(resultBuffer, resultArraySize);
                        currentIndexResponseBuffer = currentIndexResponseBuffer == _binaryResponseMessages.Count - 1 ?
                            0 : currentIndexResponseBuffer + 1;
                    }

                    Logger.Trace(L($"Received:  {message}"));
                    _lastReceivedMsg = DateTime.UtcNow;
                    _messageReceivedSubject.OnNext(message);
                } while (client.State == WebSocketState.Open && !token.IsCancellationRequested);
            }
            catch (TaskCanceledException e)
            {
                // task was canceled, ignore
                causedException = e;
            }
            catch (OperationCanceledException e)
            {
                // operation was canceled, ignore
                causedException = e;
            }
            catch (ObjectDisposedException e)
            {
                // client was disposed, ignore
                causedException = e;
            }
            catch (Exception e)
            {
                Logger.Error(e, L($"Error while listening to websocket stream, error: '{e.Message}'"));
                causedException = e;
            }


            if (ShouldIgnoreReconnection(client) || !IsStarted)
            {
                // reconnection already in progress or client stopped/disposed, do nothing
                return;
            }

            // listening thread is lost, we have to reconnect
            _ = ReconnectSynchronized(ReconnectionType.Lost, false, causedException);
        }
    }
}
