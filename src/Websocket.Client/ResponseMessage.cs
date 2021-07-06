using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;

namespace Websocket.Client
{
    /// <summary>
    /// Received message, could be Text or Binary
    /// </summary>


    public class ResponseMessage
    {
        private ResponseMessage(byte[] baseBuffer, string text, WebSocketMessageType messageType)
        {
            if(baseBuffer != null)
            {
                _binary = new ArraySegment<byte>(baseBuffer);
                _binStrucSize = Unsafe.SizeOf<ArraySegment<byte>>();
            }
            Text = text;
            MessageType = messageType;
        }

        /// <summary>
        /// Received text message (only if type = WebSocketMessageType.Text)
        /// </summary>
        public string Text { get; }
        private int _binStrucSize;
        private ArraySegment<byte> _binary;
        /// <summary>
        /// Received binary message (only if type = WebSocketMessageType.Binary)
        /// </summary>
        public ref ArraySegment<byte> Binary { get => ref _binary; }

        /// <summary>
        /// Current message type (Text or Binary)
        /// </summary>
        public WebSocketMessageType MessageType { get; }

        /// <summary>
        /// Return string info about the message
        /// </summary>
        public override string ToString()
        {
            if (MessageType == WebSocketMessageType.Text)
            {
                return Text;
            }

            return $"Type binary, length: {Binary.Count}";
        }

        ///<summary>
        ///Copies into the buffer and resizes ArraySegment
        ///</summary>
        internal unsafe void CopyInto(byte[] source, int size)
        {
            Array.Copy(source, Binary.Array, size);          
            var ptrToArray = (int*)Unsafe.AsPointer(ref _binary);
            //32 bit because pointer to array is only 4 bytes
            if(_binStrucSize == 12)
            {
                //set offset back to 0
                ptrToArray[1] = 0;
                //set count to new size
                ptrToArray[2] = size;
            }
            //64 bit because pointer to array is 8 bytes
            else if(_binStrucSize == 16)
            {
                //set offset back to 0
                ptrToArray[2] = 0;
                //set count to new size
                ptrToArray[3] = size;
            }
        }
        /// <summary>
        /// Create text response message
        /// </summary>
        public static ResponseMessage TextMessage(string data)
        {
            return new ResponseMessage(null, data, WebSocketMessageType.Text);
        }

        /// <summary>
        /// Create binary response message
        /// </summary>
        public static ResponseMessage BinaryMessage(byte[] data)
        {
            return new ResponseMessage(data, null, WebSocketMessageType.Binary);
        }
    }
}