import React, { useState, useEffect, useCallback } from 'react';
import {
  Modal,
  ModalContent,
  ModalHeader,
  ModalBody,
  Tabs,
  Tab,
  Button,
  Chip
} from '@heroui/react';
import {
  Globe,
  RefreshCw,
  Shield,
  MessageSquare,
  Hash,
  Server,
  Copy,
  Check,
  Users,
  Crown,
  Wifi,
  LogOut
} from 'lucide-react';
import { CustomSelect } from '../../shared/CustomSelect';
import { PlayerBridge } from '../services/playerBridge';
import { NetplayRoomInfo, NetplayChatMessage, NetplayPlayerInfo, PlayerInitialData } from '../../types/player';

interface InGameNetplayModalProps {
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
  config: PlayerInitialData;
}

export const InGameNetplayModal: React.FC<InGameNetplayModalProps> = ({
  isOpen,
  onOpenChange,
  config
}) => {
  const [activeTab, setActiveTab] = useState<string>('rooms');
  const [rooms, setRooms] = useState<NetplayRoomInfo[]>([]);
  const [loadingRooms, setLoadingRooms] = useState<boolean>(false);
  const [copiedId, setCopiedId] = useState<boolean>(false);

  // Host form
  const [hostRoomName, setHostRoomName] = useState<string>('');
  const [hostMaxPlayers, setHostMaxPlayers] = useState<number>(2);
  const [hostPassword, setHostPassword] = useState<string>('');

  // Active session
  const [inSession, setInSession] = useState<boolean>(false);
  const [sessionRoomName, setSessionRoomName] = useState<string>('Multiplayer Room');
  const [sessionRoomId, setSessionRoomId] = useState<string>('');
  const [sessionPassword, setSessionPassword] = useState<string>('');
  const [players, setPlayers] = useState<NetplayPlayerInfo[]>([]);
  const [currentPing, setCurrentPing] = useState<number | null>(null);
  const [chatMessages, setChatMessages] = useState<NetplayChatMessage[]>([
    { id: '1', author: 'System', text: 'Welcome to the Netplay room! Say hello.', timestamp: '', isSystem: true }
  ]);
  const [chatInput, setChatInput] = useState<string>('');

  // Nickname & Settings
  const [nickname, setNickname] = useState<string>('Player');
  const [savedNickToast, setSavedNickToast] = useState<boolean>(false);
  const [streamQuality, setStreamQuality] = useState<string>('high');

  const resolvedGameId =
    config.gameId ||
    (window as any).JellyEmuConfig?.gameId ||
    (window as any).EJS_gameID ||
    0;

  const parsePlayersList = useCallback((playersMap: any): NetplayPlayerInfo[] => {
    if (!playersMap || typeof playersMap !== 'object') return [];
    const np = (window as any).JellyEmuNetplay;
    const myName = (np?.getPlayerName ? np.getPlayerName() : '') || nickname;
    const emu = (window as any).EJS_emulator;
    const myId = emu?.netplay?.playerID || '';
    const mySocketId = emu?.netplay?.socket?.id || '';

    const keys = Object.keys(playersMap);
    keys.sort((a, b) => {
      const orderA = (playersMap[a] && playersMap[a].joinOrder) || 0;
      const orderB = (playersMap[b] && playersMap[b].joinOrder) || 0;
      return orderA - orderB;
    });

    return keys.map((key, idx) => {
      const p = playersMap[key] || {};
      const name = p.player_name || p.name || p.username || `Player ${idx + 1}`;
      const isHost = Boolean(p.isOwner === true || p.owner === true || idx === 0);
      const isMe = Boolean(
        (myId && (key === myId || p.userid === myId || p.playerId === myId)) ||
        (mySocketId && p.socketId === mySocketId) ||
        (myName && name.toLowerCase() === myName.toLowerCase())
      );
      const ping = p.ping !== undefined ? p.ping : (isMe ? np?.currentPing : null);

      const assignedNumber = typeof p.playerNumber === 'number' && p.playerNumber > 0
        ? p.playerNumber
        : (typeof p.playerIndex === 'number' && p.playerIndex >= 0 ? p.playerIndex + 1 : idx + 1);

      return {
        peerId: key,
        username: name,
        playerIndex: assignedNumber,
        isHost,
        isMe,
        pingMs: ping
      };
    });
  }, [nickname]);

  const syncNetplayState = useCallback(() => {
    const np = (window as any).JellyEmuNetplay;
    if (!np) return;

    if (np.inRoom) {
      setInSession(true);
      if (np.roomName) setSessionRoomName(np.roomName);
      if (np.roomId) setSessionRoomId(np.roomId);
      if (np.password) setSessionPassword(np.password);
      if (np.currentPing !== undefined && np.currentPing !== null) {
        setCurrentPing(np.currentPing);
      }
      const parsed = parsePlayersList(np.players);
      setPlayers(parsed);
    } else {
      setInSession(false);
      setPlayers([]);
    }
  }, [parsePlayersList]);

  const parseRoomsData = useCallback((data: any): NetplayRoomInfo[] => {
    if (!data) return [];
    if (Array.isArray(data)) {
      return data.map((r) => ({
        id: String(r.id || r.sessionId || ''),
        name: r.name || r.room_name || 'Room',
        gameId: r.gameId ?? r.game_id ?? 0,
        gameName: r.gameName || config.gameName,
        hostPlayer: r.hostPlayer || r.player_name || 'Host',
        currentPlayers: typeof r.currentPlayers === 'number' ? r.currentPlayers : (typeof r.current === 'number' ? r.current : 1),
        maxPlayers: typeof r.maxPlayers === 'number' ? r.maxPlayers : (typeof r.max === 'number' ? r.max : 2),
        isPasswordProtected: Boolean(r.isPasswordProtected || r.hasPassword || r.has_password),
        pingMs: typeof r.pingMs === 'number' ? r.pingMs : (typeof r.host_ping === 'number' ? r.host_ping : (typeof r.ping === 'number' ? r.ping : undefined))
      }));
    }
    if (typeof data === 'object') {
      return Object.keys(data).map((key) => {
        const r = data[key] || {};
        return {
          id: String(key),
          name: r.name || r.room_name || `Room #${key.substring(0, 5)}`,
          gameId: r.gameId ?? r.game_id ?? 0,
          gameName: r.gameName || config.gameName,
          hostPlayer: r.hostPlayer || r.player_name || 'Host',
          currentPlayers: typeof r.currentPlayers === 'number' ? r.currentPlayers : (typeof r.current === 'number' ? r.current : 1),
          maxPlayers: typeof r.maxPlayers === 'number' ? r.maxPlayers : (typeof r.max === 'number' ? r.max : 2),
          isPasswordProtected: Boolean(r.isPasswordProtected || r.hasPassword || r.has_password),
          pingMs: typeof r.pingMs === 'number' ? r.pingMs : (typeof r.host_ping === 'number' ? r.host_ping : (typeof r.ping === 'number' ? r.ping : undefined))
        };
      });
    }
    return [];
  }, [config.gameName]);

  const fetchRooms = useCallback(async () => {
    setLoadingRooms(true);
    try {
      const gidParam = resolvedGameId ? `&gameId=${encodeURIComponent(resolvedGameId)}&game_id=${encodeURIComponent(resolvedGameId)}` : '';
      const resp = await fetch(PlayerBridge.getUrl(`/jellyemu/netplay/rooms?t=${Date.now()}${gidParam}`));
      if (resp.ok) {
        const rawData = await resp.json();
        setRooms(parseRoomsData(rawData));
      } else {
        const fallbackResp = await fetch(PlayerBridge.getUrl(`/jellyemu/netplay/list?t=${Date.now()}${gidParam}`));
        if (fallbackResp.ok) {
          const rawData = await fallbackResp.json();
          setRooms(parseRoomsData(rawData));
        } else {
          setRooms([]);
        }
      }
    } catch {
      setRooms([]);
    }
    setLoadingRooms(false);
  }, [resolvedGameId, parseRoomsData]);

  // Initial mount & modal open synchronization
  useEffect(() => {
    if (isOpen) {
      fetchRooms();
      const np = (window as any).JellyEmuNetplay;
      if (np) {
        if (typeof np.getPlayerName === 'function') {
          const stored = np.getPlayerName();
          if (stored) setNickname(stored);
        }
        syncNetplayState();
        if (np.inRoom) {
          setActiveTab('session');
        }
      }
    }
  }, [isOpen, syncNetplayState, fetchRooms]);

  // Auto-refresh room list periodically while on the Rooms tab
  useEffect(() => {
    if (!isOpen || activeTab !== 'rooms' || inSession) return;
    const interval = setInterval(() => {
      fetchRooms();
    }, 3000);
    return () => clearInterval(interval);
  }, [isOpen, activeTab, inSession, fetchRooms]);

  // Periodic state refresh when in active session
  useEffect(() => {
    if (!isOpen || !inSession) return;
    const timer = setInterval(() => {
      syncNetplayState();
    }, 1500);
    return () => clearInterval(timer);
  }, [isOpen, inSession, syncNetplayState]);

  // Global Netplay window event listeners
  useEffect(() => {
    const handleJoined = (e: any) => {
      const detail = e?.detail || {};
      setInSession(true);
      if (detail.roomName) setSessionRoomName(detail.roomName);
      if (detail.roomId) setSessionRoomId(detail.roomId);
      if (detail.password) setSessionPassword(detail.password);
      if (detail.players) {
        setPlayers(parsePlayersList(detail.players));
      }
      setActiveTab('session');
    };

    const handlePlayersChanged = (e: any) => {
      const detail = e?.detail || {};
      if (detail.players) {
        setPlayers(parsePlayersList(detail.players));
      }
    };

    const handleLeft = () => {
      setInSession(false);
      setPlayers([]);
      setActiveTab('rooms');
      fetchRooms();
    };

    const handleChat = (e: any) => {
      const detail = e?.detail;
      if (detail && detail.text) {
        setChatMessages((prev) => {
          const last = prev[prev.length - 1];
          if (last && last.author === detail.author && last.text === detail.text) {
            return prev;
          }
          return [
            ...prev,
            {
              id: typeof crypto !== 'undefined' && crypto.randomUUID ? crypto.randomUUID() : String(Date.now() + '_' + prev.length),
              author: detail.author || 'Player',
              text: detail.text,
              timestamp: detail.timestamp || new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
            }
          ];
        });
      }
    };

    window.addEventListener('jellyemu:netplay-joined', handleJoined);
    window.addEventListener('jellyemu:netplay-players-changed', handlePlayersChanged);
    window.addEventListener('jellyemu:netplay-left', handleLeft);
    window.addEventListener('jellyemu:netplay-chat', handleChat);

    return () => {
      window.removeEventListener('jellyemu:netplay-joined', handleJoined);
      window.removeEventListener('jellyemu:netplay-players-changed', handlePlayersChanged);
      window.removeEventListener('jellyemu:netplay-left', handleLeft);
      window.removeEventListener('jellyemu:netplay-chat', handleChat);
    };
  }, [parsePlayersList]);

  const handleCreateRoom = () => {
    const np = (window as any).JellyEmuNetplay;
    if (np && typeof np.createRoom === 'function') {
      const rName = hostRoomName.trim() || `${nickname}'s Game`;
      setSessionRoomName(rName);
      setSessionPassword(hostPassword);
      np.createRoom({
        name: rName,
        maxPlayers: hostMaxPlayers,
        password: hostPassword,
        gameId: resolvedGameId
      });
      setInSession(true);
      setActiveTab('session');
    }
  };

  const handleJoinRoom = (room: NetplayRoomInfo) => {
    const np = (window as any).JellyEmuNetplay;
    if (np && typeof np.joinRoom === 'function') {
      let pwd = '';
      if (room.isPasswordProtected) {
        pwd = prompt('Enter Room Password:') || '';
      }
      setSessionRoomName(room.name || 'Multiplayer Room');
      setSessionRoomId(room.id);
      setSessionPassword(pwd);
      np.joinRoom(room.id, pwd);
      setInSession(true);
      setActiveTab('session');
    }
  };

  const handleLeaveRoom = () => {
    setInSession(false);
    setPlayers([]);
    setActiveTab('rooms');
    const np = (window as any).JellyEmuNetplay;
    if (np && typeof np.leaveRoom === 'function') {
      try {
        np.leaveRoom();
      } catch (err) {
        console.warn('[JellyEmu Netplay] np.leaveRoom error:', err);
      }
    }
    fetchRooms();
  };

  const handleSendChat = () => {
    if (!chatInput.trim()) return;
    const np = (window as any).JellyEmuNetplay;
    const text = chatInput.trim();
    if (np && typeof np.sendChat === 'function') {
      np.sendChat(text);
    } else {
      setChatMessages((prev) => [
        ...prev,
        {
          id: typeof crypto !== 'undefined' && crypto.randomUUID ? crypto.randomUUID() : String(Date.now() + '_' + prev.length),
          author: nickname,
          text: text,
          timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
        }
      ]);
    }
    setChatInput('');
  };

  const handleNicknameChange = (val: string) => {
    setNickname(val);
    const np = (window as any).JellyEmuNetplay;
    if (np && typeof np.setPlayerName === 'function') {
      np.setPlayerName(val);
    }
  };

  const handleSaveNicknameBtn = () => {
    const np = (window as any).JellyEmuNetplay;
    if (np && typeof np.setPlayerName === 'function') {
      np.setPlayerName(nickname);
    }
    setSavedNickToast(true);
    setTimeout(() => setSavedNickToast(false), 2000);
  };

  const handleCopyGameId = () => {
    navigator.clipboard.writeText(String(resolvedGameId));
    setCopiedId(true);
    setTimeout(() => setCopiedId(false), 2000);
  };

  return (
    <Modal
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      size="xl"
      backdrop="blur"
      placement="center"
      scrollBehavior="inside"
      classNames={{
        base: 'bg-[#121216] border border-white/10 text-white shadow-2xl rounded-2xl max-w-xl',
        header: 'border-b border-white/10 px-6 py-4 flex items-center justify-between',
        body: 'p-6 flex flex-col gap-5',
        closeButton: 'text-neutral-400 hover:text-white hover:bg-white/10 p-1.5 rounded-lg'
      }}
    >
      <ModalContent>
        {() => (
          <>
            <ModalHeader>
              <div className="flex items-center justify-between w-full pr-6">
                <div className="flex items-center gap-2.5">
                  <Globe className="w-5 h-5 text-white" />
                  <div className="flex items-center gap-2">
                    <h2 className="text-base font-semibold tracking-wide">Multiplayer Netplay</h2>
                    <Chip
                      size="sm"
                      variant="flat"
                      className="bg-white/10 text-neutral-300 font-mono text-[11px] border border-white/10"
                    >
                      Game ID: #{resolvedGameId}
                    </Chip>
                  </div>
                </div>

                {inSession && (
                  <Chip
                    size="sm"
                    variant="flat"
                    className="bg-emerald-500/20 text-emerald-400 text-[10px] font-bold uppercase border border-emerald-500/30"
                  >
                    Active Match
                  </Chip>
                )}
              </div>
            </ModalHeader>

            <ModalBody className="flex flex-col gap-5">
              {/* Tab Navigation */}
              <Tabs
                selectedKey={activeTab}
                onSelectionChange={(key) => setActiveTab(key as string)}
                variant="solid"
                classNames={{
                  tabList: 'bg-black/50 p-1 rounded-xl border border-white/10 w-full',
                  cursor: 'bg-white text-black rounded-lg shadow-sm',
                  tab: 'h-8 text-xs font-medium text-neutral-400 data-[selected=true]:text-black transition-colors',
                  tabContent: 'group-data-[selected=true]:text-black font-semibold'
                }}
              >
                <Tab key="rooms" title="Rooms" />
                <Tab key="host" title="Host Game" />
                {inSession && <Tab key="session" title="Active Match" />}
                <Tab key="settings" title="Settings" />
              </Tabs>

              {/* Rooms Browser */}
              {activeTab === 'rooms' && (
                <div className="flex flex-col gap-3">
                  <div className="flex items-center justify-between">
                    <div className="flex flex-col">
                      <span className="text-xs font-semibold text-white uppercase tracking-wider">
                        Available Rooms
                      </span>
                      <span className="text-[11px] text-neutral-400 mt-0.5">
                        Filtered for Game ID #{resolvedGameId} ({config.gameName})
                      </span>
                    </div>

                    <Button
                      size="sm"
                      variant="flat"
                      isLoading={loadingRooms}
                      onPress={fetchRooms}
                      className="h-7 px-2.5 text-xs bg-white/5 hover:bg-white/10 text-neutral-300 border border-white/10 rounded-lg"
                      startContent={<RefreshCw className="w-3 h-3" />}
                    >
                      Refresh
                    </Button>
                  </div>

                  <div className="flex flex-col gap-2 max-h-64 overflow-y-auto pr-1">
                    {loadingRooms ? (
                      <div className="text-xs text-neutral-500 py-6 text-center">Scanning for active rooms…</div>
                    ) : rooms.length === 0 ? (
                      <div className="text-xs text-neutral-500 py-8 text-center flex flex-col items-center gap-2 rounded-xl bg-[#16161a] border border-white/5">
                        <Globe className="w-6 h-6 text-neutral-600" />
                        <span>No active rooms found for Game ID #{resolvedGameId}.</span>
                        <span className="text-[11px] text-neutral-500">Host a game to invite other players to join!</span>
                      </div>
                    ) : (
                      rooms.map((room) => (
                        <div
                          key={room.id}
                          className="flex items-center justify-between p-3.5 rounded-xl bg-[#16161a] border border-white/10 hover:border-white/20 transition-all"
                        >
                          <div className="flex flex-col">
                            <div className="flex items-center gap-2">
                              <span className="text-xs font-semibold text-white">{room.name}</span>
                              {room.isPasswordProtected && (
                                <span title="Password Protected">
                                  <Shield className="w-3 h-3 text-amber-400" />
                                </span>
                              )}
                            </div>
                            <span className="text-[11px] text-neutral-400 mt-0.5">
                              Host: {room.hostPlayer} • Players: {room.currentPlayers}/{room.maxPlayers}
                            </span>
                          </div>

                          <Button
                            size="sm"
                            variant="solid"
                            onPress={() => handleJoinRoom(room)}
                            className="h-7 px-3 text-xs bg-white text-black hover:bg-neutral-200 font-medium rounded-lg"
                          >
                            Join
                          </Button>
                        </div>
                      ))
                    )}
                  </div>
                </div>
              )}

              {/* Host Game */}
              {activeTab === 'host' && (
                <div className="flex flex-col gap-4">
                  <div className="flex items-center justify-between">
                    <span className="text-xs font-semibold text-neutral-400 uppercase tracking-wider">
                      Host a Netplay Room
                    </span>
                    <span className="text-[11px] font-mono text-neutral-400">
                      Game ID: #{resolvedGameId}
                    </span>
                  </div>

                  <div className="flex flex-col gap-1.5">
                    <label className="text-xs text-neutral-300 font-medium">Room Name</label>
                    <input
                      type="text"
                      placeholder={`${nickname}'s Game`}
                      value={hostRoomName}
                      onChange={(e) => setHostRoomName(e.target.value)}
                      className="h-9 px-3 text-xs bg-[#16161a] border border-white/15 rounded-xl text-white outline-none focus:border-white/40"
                    />
                  </div>

                  <div className="flex flex-col gap-1.5">
                    <label className="text-xs text-neutral-300 font-medium">Max Players</label>
                    <CustomSelect
                      value={hostMaxPlayers}
                      onChange={(val) => setHostMaxPlayers(parseInt(val, 10) || 2)}
                      options={[
                        { value: '2', label: '2 Players' },
                        { value: '3', label: '3 Players' },
                        { value: '4', label: '4 Players' }
                      ]}
                      size="sm"
                    />
                  </div>

                  <div className="flex flex-col gap-1.5">
                    <label className="text-xs text-neutral-300 font-medium">Room Password (Optional)</label>
                    <input
                      type="password"
                      placeholder="Leave blank for public room"
                      value={hostPassword}
                      onChange={(e) => setHostPassword(e.target.value)}
                      className="h-9 px-3 text-xs bg-[#16161a] border border-white/15 rounded-xl text-white outline-none focus:border-white/40"
                    />
                  </div>

                  <Button
                    size="sm"
                    variant="solid"
                    onPress={handleCreateRoom}
                    className="h-8 text-xs bg-white text-black hover:bg-neutral-200 font-semibold mt-2 rounded-xl"
                  >
                    Create &amp; Host Room
                  </Button>
                </div>
              )}

              {/* Active Session */}
              {activeTab === 'session' && inSession && (
                <div className="flex flex-col gap-4">
                  {/* Room Status Banner */}
                  <div className="flex items-center justify-between p-3.5 rounded-xl bg-[#16161a] border border-white/10">
                    <div className="flex flex-col min-w-0 pr-2">
                      <div className="flex items-center gap-2">
                        <span className="text-xs font-semibold text-white truncate">{sessionRoomName}</span>
                        {sessionPassword && (
                          <span title="Password Protected">
                            <Shield className="w-3 h-3 text-amber-400 shrink-0" />
                          </span>
                        )}
                      </div>
                      <span className="text-[11px] text-neutral-400 mt-0.5">
                        Game ID #{resolvedGameId} {sessionRoomId ? `• Room ID: ${sessionRoomId}` : ''}
                      </span>
                    </div>
                    <Button
                      size="sm"
                      variant="flat"
                      onPress={handleLeaveRoom}
                      className="h-7 px-3 text-xs bg-red-500/10 hover:bg-red-500/20 text-red-400 border border-red-500/20 font-medium rounded-lg shrink-0"
                      startContent={<LogOut className="w-3 h-3" />}
                    >
                      Leave Room
                    </Button>
                  </div>

                  {/* Connected Players Section */}
                  <div className="flex flex-col gap-2">
                    <div className="flex items-center justify-between">
                      <div className="text-xs font-semibold text-neutral-400 uppercase tracking-wider flex items-center gap-1.5">
                        <Users className="w-3.5 h-3.5" />
                        Connected Players ({players.length})
                      </div>
                      {currentPing !== null && (
                        <div className="flex items-center gap-1 text-[11px] text-neutral-400 font-mono">
                          <Wifi className="w-3 h-3 text-emerald-400" />
                          <span>{currentPing} ms</span>
                        </div>
                      )}
                    </div>

                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
                      {players.length === 0 ? (
                        <div className="col-span-full py-4 text-center text-xs text-neutral-500 bg-[#16161a] border border-white/5 rounded-xl">
                          Waiting for players to connect…
                        </div>
                      ) : (
                        players.map((p, idx) => {
                          const pingMs = p.pingMs;
                          const pingColor =
                            pingMs == null
                              ? 'text-neutral-500'
                              : pingMs < 80
                              ? 'text-emerald-400'
                              : pingMs < 150
                              ? 'text-amber-400'
                              : 'text-red-400';

                          return (
                            <div
                              key={p.peerId || idx}
                              className="flex items-center justify-between p-3 rounded-xl bg-[#16161a] border border-white/10"
                            >
                              <div className="flex items-center gap-2.5 min-w-0 pr-2">
                                <span className="w-6 h-6 rounded-lg bg-white/10 flex items-center justify-center text-[11px] font-mono font-bold text-white shrink-0">
                                  P{p.playerIndex || idx + 1}
                                </span>
                                <div className="flex flex-col min-w-0">
                                  <div className="flex items-center gap-1.5 flex-wrap">
                                    <span className="text-xs font-semibold text-white truncate max-w-[110px]" title={p.username}>
                                      {p.username}
                                    </span>
                                    {p.isHost && (
                                      <Chip
                                        size="sm"
                                        variant="flat"
                                        className="bg-amber-500/20 text-amber-300 border border-amber-500/30 text-[9px] font-bold px-1.5 h-4"
                                        startContent={<Crown className="w-2.5 h-2.5" />}
                                      >
                                        HOST
                                      </Chip>
                                    )}
                                    {p.isMe && (
                                      <Chip
                                        size="sm"
                                        variant="flat"
                                        className="bg-emerald-500/20 text-emerald-300 border border-emerald-500/30 text-[9px] font-bold px-1.5 h-4"
                                      >
                                        YOU
                                      </Chip>
                                    )}
                                  </div>
                                </div>
                              </div>

                              {pingMs !== undefined && pingMs !== null ? (
                                <div className={`flex items-center gap-1 text-[11px] font-mono ${pingColor} shrink-0`}>
                                  <span className="w-1.5 h-1.5 rounded-full bg-current"></span>
                                  <span>{pingMs}ms</span>
                                </div>
                              ) : (
                                <div className="text-[10px] text-neutral-500 font-mono shrink-0">
                                  {p.isHost ? 'Host' : 'Active'}
                                </div>
                              )}
                            </div>
                          );
                        })
                      )}
                    </div>
                  </div>

                  {/* Chat Box */}
                  <div className="flex flex-col gap-2">
                    <div className="text-xs font-semibold text-neutral-400 uppercase tracking-wider flex items-center gap-1.5">
                      <MessageSquare className="w-3.5 h-3.5" />
                      In-Game Chat
                    </div>

                    <div className="h-32 overflow-y-auto p-3 rounded-xl bg-[#16161a] border border-white/10 flex flex-col gap-2 text-xs">
                      {chatMessages.map((msg) => (
                        <div key={msg.id} className="leading-snug">
                          <span className={`font-semibold ${msg.isSystem ? 'text-amber-400' : 'text-neutral-300'}`}>
                            {msg.author}:{' '}
                          </span>
                          <span className="text-neutral-200">{msg.text}</span>
                        </div>
                      ))}
                    </div>

                    <div className="flex gap-2">
                      <input
                        type="text"
                        placeholder="Type a message..."
                        value={chatInput}
                        onChange={(e) => setChatInput(e.target.value)}
                        onKeyDown={(e) => e.key === 'Enter' && handleSendChat()}
                        className="flex-1 h-9 px-3 text-xs bg-[#16161a] border border-white/15 rounded-xl text-white outline-none focus:border-white/40"
                      />
                      <Button
                        size="sm"
                        variant="solid"
                        onPress={handleSendChat}
                        className="h-9 px-4 text-xs bg-white text-black hover:bg-neutral-200 font-medium rounded-xl"
                      >
                        Send
                      </Button>
                    </div>
                  </div>
                </div>
              )}

              {/* Netplay Settings & Game ID Info */}
              {activeTab === 'settings' && (
                <div className="flex flex-col gap-4">
                  {/* Matchmaking & Game ID Card */}
                  <div className="p-4 rounded-xl bg-[#16161a] border border-white/10 flex flex-col gap-3">
                    <div className="flex items-center justify-between">
                      <div className="flex items-center gap-2">
                        <Hash className="w-4 h-4 text-white" />
                        <span className="text-xs font-semibold text-white uppercase tracking-wider">
                          Netplay Game ID
                        </span>
                      </div>
                      <Button
                        size="sm"
                        variant="flat"
                        onPress={handleCopyGameId}
                        className="h-6 px-2 text-[11px] bg-white/10 hover:bg-white/15 text-neutral-200 border border-white/10 rounded-lg"
                        startContent={copiedId ? <Check className="w-3 h-3 text-emerald-400" /> : <Copy className="w-3 h-3" />}
                      >
                        {copiedId ? 'Copied' : 'Copy ID'}
                      </Button>
                    </div>

                    <div className="grid grid-cols-2 gap-2 text-xs">
                      <div className="p-3 rounded-xl bg-black/40 border border-white/5 flex flex-col">
                        <span className="text-[11px] text-neutral-400">Matchmaking ID</span>
                        <span className="text-sm font-bold text-white font-mono mt-0.5">#{resolvedGameId}</span>
                      </div>
                      <div className="p-3 rounded-xl bg-black/40 border border-white/5 flex flex-col">
                        <span className="text-[11px] text-neutral-400 flex items-center gap-1">
                          <Server className="w-3 h-3" /> Relay Server
                        </span>
                        <span
                          className="text-xs font-semibold text-white truncate mt-0.5"
                          title={config.netplayServer || 'Jellyfin Built-in Relay'}
                        >
                          {config.netplayServer || 'Jellyfin Relay'}
                        </span>
                      </div>
                    </div>

                    <div className="text-[11px] text-neutral-400">
                      Matchmaking rooms and peer connections are strictly grouped by this unique Game ID to guarantee that all connected players run the identical game ROM.
                    </div>
                  </div>

                  {/* Player Profile */}
                  <div className="flex flex-col gap-3">
                    <div className="text-xs font-semibold text-neutral-400 uppercase tracking-wider">
                      Player Profile
                    </div>

                    <div className="flex flex-col gap-1.5">
                      <label className="text-xs text-neutral-300 font-medium">Display Nickname</label>
                      <div className="flex gap-2">
                        <input
                          type="text"
                          value={nickname}
                          maxLength={20}
                          onChange={(e) => handleNicknameChange(e.target.value)}
                          className="flex-1 h-9 px-3 text-xs bg-[#16161a] border border-white/15 rounded-xl text-white outline-none focus:border-white/40"
                          placeholder="Your Nickname"
                        />
                        <Button
                          size="sm"
                          variant="flat"
                          onPress={handleSaveNicknameBtn}
                          className="h-9 px-3 text-xs bg-white/10 hover:bg-white/15 text-white border border-white/10 rounded-xl font-medium"
                        >
                          {savedNickToast ? 'Saved!' : 'Save'}
                        </Button>
                      </div>
                    </div>

                    <div className="flex flex-col gap-1.5 pb-6">
                      <label className="text-xs text-neutral-300 font-medium">Video Streaming Quality</label>
                      <CustomSelect
                        dropUp
                        value={streamQuality}
                        onChange={(val) => {
                          setStreamQuality(val);
                          try {
                            if (window.localStorage) {
                              localStorage.setItem('je_np_stream_quality', val);
                            }
                          } catch (_) {}
                        }}
                        options={[
                          { value: 'low', label: 'Low (Canvas 480p - Low Bandwidth)' },
                          { value: 'balanced', label: 'Balanced (Canvas 720p - Standard)' },
                          { value: 'high', label: 'High (Canvas 1:1 - Best Quality)' }
                        ]}
                        size="sm"
                      />
                    </div>
                  </div>
                </div>
              )}
            </ModalBody>
          </>
        )}
      </ModalContent>
    </Modal>
  );
};
