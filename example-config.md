Here's an example configuration file.

    {
      /* set this to "trace" or "debug" if you want
       * to debug Warpdrive, otherwise "info" is a 
       * sane default, and "warn" will only let you
       * know if something goes wrong. */
      "log.verbosity": "info", 
      
      /* set this to "true" if you wish to host a 
       * Warpdrive server (unsupported on Windows),
       * false otherwise. */                            
      "network.server": false, 
      
      /* network.connect must be set to the URI of
       * your Warpdrive server. supported protocols
       * are: tcp, udp, ws */            
      "network.connect": "udp://your.server.ip.here:3333",
      
      /* attempts to automatically configure your
       * network routes so that you access the 
       * Internet over your Warpdrive connection.
       * set this to "false" if you wish to use
       * Warpdrive as a true VPN, with no access
       * to the outside Internet. */
      "network.autoconf": true,
      
      /* sets your tun device's IP, default gateway
       * and subnet mask. the 10.0.0.0/8 subnet is
       * a popular choice with VPNs. */
      "network.tun.addr": "10.0.0.20/8",
      
      /* forces UDP pMTUd to assume and report an
       * MTU of 1400 bytes. this is an experimental
       * setting for problematic networks, do not
       * set if your network operates fine without
       * this setting. */
      /* "network.udp.force_mtu": 1400 */
      
      /* spoofs Warpdrive's Websocket handshake to
       * appear as if it originated from and sent 
       * to the given hosts. useful for traversing
       * transparent HTTP proxies. */
      "network.websocket.spoofed_host": "google.com",
      "network.websocket.spoofed_origin": "google.com"
    }
