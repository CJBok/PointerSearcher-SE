using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Diagnostics;

namespace PointerSearcher
{
    class MemoryUtils
    {
        Socket _socket;

        public MemoryUtils(Socket s) {
            _socket = s;
        }

        public List<byte> ReadByteArray( UInt64 address, UInt32 offset, UInt32 size, bool debug = false )
        {
            size =  size <= 0x10 ? 0x10 : size / 0x10 * 0x10;
            var outbuf = new byte[size];
            readmemblock( ref outbuf, address + offset, size );

            //if ( debug ) Console.WriteLine( $"{address:X} + {offset:X} = {value:X}" );
            return new List<byte>(outbuf);
        }

        public UInt64 ReadU64( UInt64 address, UInt32 offset, bool debug = false ) {
            var outbuf = new byte[8];
            readmemblock( ref outbuf, address + offset, 8 );
            var value = BitConverter.ToUInt64( outbuf, 0 );

            if ( debug ) Console.WriteLine( $"{address:X} + {offset:X} = {value:X}" );
            return value;
        }

        public Int32 ReadS32( UInt64 address, UInt32 offset, bool debug = false )
        {
            var outbuf = new byte[4];
            readmemblock( ref outbuf, address + offset, 4 );
            var value = BitConverter.ToInt32( outbuf, 0 );

            if (debug) Console.WriteLine( $"{address:X} + {offset:X} = {value}" );
            return value;
        }


        private bool readmemblock( ref byte[] outbuf, UInt64 address, uint size )
        {
            byte[] k = new byte[5];
            uint len;
            uint pos = 0;
            byte[] inbuf;
            int a = SendMessage( NoexsCommands.ReadMem );
            a = SendData( BitConverter.GetBytes( address ) );
            a = SendData( BitConverter.GetBytes( size ) );
            if ( noerror() )
            {
                while ( size > 0 )
                {
                    if ( noerror() )
                    {
                        while ( _socket.Available < 5 ) { }
                        _socket.Receive( k );
                        len = BitConverter.ToUInt32( k, 1 );
                        if ( k[0] == 0 ) // no compression
                        {
                            inbuf = new byte[len];
                            while ( _socket.Available < len ) { }
                            _socket.Receive( inbuf );
                            for ( int i = 0; i < len; i++ )
                                outbuf[pos + i] = inbuf[i];
                            pos += len;
                            size -= len;
                        }
                        else
                        {
                            k = new byte[4];
                            while ( _socket.Available < 4 ) { }
                            _socket.Receive( k );
                            int rlesize = BitConverter.ToInt32( k, 0 );
                            inbuf = new byte[rlesize];
                            while ( _socket.Available < rlesize ) { }
                            _socket.Receive( inbuf );
                            uint urlesize = 0;
                            for ( int i = 0; urlesize < len; i += 2 )
                            {
                                for ( int m = 0; m < inbuf[1]; m++ )
                                    outbuf[pos + urlesize + m] = inbuf[i];
                                urlesize += inbuf[i + 1];
                            }
                            pos += urlesize;
                            size -= urlesize;
                        }
                    }
                }
            }
            return noerror();
        }

        

        private int SendMessage( NoexsCommands cmd )
        {
            return _socket.Send( new byte[] { (byte)cmd } );
        }

        private int SendData( byte[] data )
        {
            return _socket.Send( data );
        }

        private bool noerror()
        {
            while ( _socket.Available < 4 ) { }
            byte[] b = new byte[4];
            _socket.Receive( b );
            return !showerror( b );
        }

        private bool showerror( byte[] b )
        {
            string error = Convert.ToString( b[0] ) + " . " + Convert.ToString( b[1] ) + " . " + Convert.ToString( b[2] ) + " . " + Convert.ToString( b[3] );
            if ( b[0] == 15 && b[1] == 8 )
            {
                error += "  pminfo not valid";
            }
            if ( b[0] == 93 && b[1] == 21 )
            {
                error += "  already attached";
            }
            if ( b[0] == 93 && b[1] == 19 )
            {
                error += "  invalid cmd";
            }
            if ( b[0] == 93 && b[1] == 33 )
            {
                error += "  user abort";
            }
            if ( b[0] == 93 && b[1] == 35 )
            {
                error += "  file not accessible";
            }
            int e = BitConverter.ToInt32( b, 0 );
            return e != 0;
        }
    }
}
