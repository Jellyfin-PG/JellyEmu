import { Card, CardBody, Button, Chip, Spinner, Table, TableHeader, TableColumn, TableBody, TableRow, TableCell, Tooltip } from '@heroui/react';
import { Gamepad2, Clock, Users, Trophy, RefreshCw, Layers, Award, Flame } from 'lucide-react';
import { motion } from 'framer-motion';
import { useStatistics } from '../../hooks/useStatistics';

export const StatisticsTab: React.FC = () => {
  const { playtimeStats, libraryStats, userGames, loading, reload, formatDuration } = useStatistics();

  if (loading) {
    return (
      <div className="flex flex-col items-center justify-center p-16 space-y-3">
        <Spinner size="lg" color="primary" />
        <p className="text-sm text-slate-300 font-medium">Analyzing games catalog & playtime telemetry...</p>
      </div>
    );
  }

  const topPlatform = libraryStats.platforms?.[0]?.platform || 'None';
  const topUser = playtimeStats.users?.[0]?.username || 'No sessions yet';
  const topGamesList = (playtimeStats.topGames && playtimeStats.topGames.length > 0)
    ? playtimeStats.topGames
    : (userGames?.games || []);

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4">
        <div>
          <h3 className="text-xl font-bold text-white flex items-center gap-2">
            <Trophy className="w-5 h-5 text-primary" />
            Library & Gameplay Telemetry
          </h3>
          <p className="text-sm text-slate-300">
            Comprehensive analytics on your retro games catalog, platform distribution, and server-wide gamer activity.
          </p>
        </div>

        <Tooltip content="Refresh Statistics">
          <Button
            size="sm"
            variant="flat"
            color="primary"
            startContent={<RefreshCw className="w-3.5 h-3.5" />}
            onPress={reload}
          >
            Refresh Data
          </Button>
        </Tooltip>
      </div>

      {/* KPI Metrics Grid */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        {/* Total Games */}
        <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm hover:border-primary/40 transition-colors">
          <CardBody className="p-5 flex items-center gap-4">
            <div className="w-12 h-12 rounded-2xl bg-primary/15 border border-primary/30 flex items-center justify-center text-primary shadow-sm shadow-primary/20 shrink-0">
              <Gamepad2 className="w-6 h-6" />
            </div>
            <div className="flex-1 min-w-0 flex flex-col items-center justify-center text-center">
              <p className="text-xs text-slate-400 font-medium uppercase tracking-wider text-center">Total Games</p>
              <h4 className="text-2xl font-bold text-white tracking-tight text-center">
                {libraryStats.totalGames.toLocaleString()}
              </h4>
            </div>
          </CardBody>
        </Card>

        {/* Total Server Playtime */}
        <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm hover:border-emerald-500/40 transition-colors">
          <CardBody className="p-5 flex items-center gap-4">
            <div className="w-12 h-12 rounded-2xl bg-emerald-500/15 border border-emerald-500/30 flex items-center justify-center text-emerald-400 shadow-sm shadow-emerald-500/20 shrink-0">
              <Clock className="w-6 h-6" />
            </div>
            <div className="flex-1 min-w-0 flex flex-col items-center justify-center text-center">
              <p className="text-xs text-slate-400 font-medium uppercase tracking-wider text-center">Total Playtime</p>
              <h4 className="text-2xl font-bold text-white tracking-tight text-center">
                {formatDuration(playtimeStats.totalSeconds)}
              </h4>
            </div>
          </CardBody>
        </Card>

        {/* Active Gamers */}
        <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm hover:border-purple-500/40 transition-colors">
          <CardBody className="p-5 flex items-center gap-4">
            <div className="w-12 h-12 rounded-2xl bg-purple-500/15 border border-purple-500/30 flex items-center justify-center text-purple-400 shadow-sm shadow-purple-500/20 shrink-0">
              <Users className="w-6 h-6" />
            </div>
            <div className="flex-1 min-w-0 flex flex-col items-center justify-center text-center">
              <p className="text-xs text-slate-400 font-medium uppercase tracking-wider text-center">Active Gamers</p>
              <h4 className="text-2xl font-bold text-white tracking-tight text-center">
                {playtimeStats.users?.length || 0}
              </h4>
            </div>
          </CardBody>
        </Card>

        {/* Top Platform */}
        <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm hover:border-amber-500/40 transition-colors">
          <CardBody className="p-5 flex items-center gap-4">
            <div className="w-12 h-12 rounded-2xl bg-amber-500/15 border border-amber-500/30 flex items-center justify-center text-amber-400 shadow-sm shadow-amber-500/20 shrink-0">
              <Layers className="w-6 h-6" />
            </div>
            <div className="flex-1 min-w-0 flex flex-col items-center justify-center text-center">
              <p className="text-xs text-slate-400 font-medium uppercase tracking-wider text-center">Largest Platform</p>
              <h4 className="text-lg font-bold text-white tracking-tight truncate text-center max-w-full" title={topPlatform}>
                {topPlatform}
              </h4>
            </div>
          </CardBody>
        </Card>
      </div>

      {/* Platform & Console Breakdown Section */}
      <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm">
        <CardBody className="p-5 sm:p-6 space-y-4">
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2">
            <div className="flex items-center gap-2.5">
              <div className="w-8 h-8 rounded-xl bg-primary/15 border border-primary/30 flex items-center justify-center text-primary">
                <Layers className="w-4 h-4" />
              </div>
              <div>
                <h4 className="font-bold text-white text-base">Games by Platform & System</h4>
                <p className="text-xs text-slate-400">
                  Distribution of {libraryStats.totalGames} retro titles across {libraryStats.platforms.length} supported systems
                </p>
              </div>
            </div>
          </div>

          {libraryStats.platforms.length === 0 ? (
            <div className="text-center py-10 text-slate-400 text-xs bg-white/[0.02] rounded-xl border border-white/5 space-y-1">
              <Gamepad2 className="w-8 h-8 mx-auto opacity-40 text-slate-500" />
              <p className="font-semibold text-slate-300">No games found in your Jellyfin library</p>
              <p className="text-slate-500">Scan your library or download ROMs from the Marketplace tab.</p>
            </div>
          ) : (
            <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-3.5 pt-1">
              {libraryStats.platforms.map((item) => (
                <div
                  key={item.platform}
                  className="p-3.5 rounded-xl bg-white/[0.03] border border-white/5 hover:border-white/15 transition-all space-y-2.5 relative overflow-hidden group"
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-bold text-white text-sm truncate" title={item.platform}>
                      {item.platform}
                    </span>
                    <Chip
                      size="sm"
                      variant="flat"
                      className="text-[11px] font-mono font-semibold h-5 shrink-0"
                      style={{
                        backgroundColor: `${item.color}25`,
                        color: item.color,
                        borderColor: `${item.color}40`
                      }}
                    >
                      {item.percentage}%
                    </Chip>
                  </div>

                  <div className="space-y-1.5">
                    <div className="flex justify-between text-[11px] text-slate-400 font-medium">
                      <span>{item.count} {item.count === 1 ? 'game' : 'games'}</span>
                      <span className="font-mono text-slate-500">{item.count}/{libraryStats.totalGames}</span>
                    </div>
                    <div
                      className="w-full rounded-full bg-white/10 overflow-hidden relative"
                      style={{ height: '8px', minHeight: '8px' }}
                    >
                      <motion.div
                        initial={{ width: 0 }}
                        animate={{ width: `${Math.min(100, Math.max(item.percentage > 0 ? 2.5 : 0, item.percentage))}%` }}
                        transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1] }}
                        style={{
                          height: '100%',
                          minHeight: '8px',
                          backgroundColor: item.color,
                          boxShadow: `0 0 10px ${item.color}aa`,
                          borderRadius: '9999px',
                          display: 'block'
                        }}
                      />
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </CardBody>
      </Card>

      {/* User Leaderboard & Server-Wide Top Games Grid */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-5">
        {/* Gamer Leaderboard */}
        <Card className="lg:col-span-2 bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm">
          <CardBody className="p-5 sm:p-6 space-y-4">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2.5">
                <div className="w-8 h-8 rounded-xl bg-warning/15 border border-warning/30 flex items-center justify-center text-warning">
                  <Award className="w-4 h-4" />
                </div>
                <div>
                  <h4 className="font-bold text-white text-base">Gamer Leaderboard</h4>
                  <p className="text-xs text-slate-400">Total verified in-game session durations per Jellyfin user</p>
                </div>
              </div>
              {playtimeStats.users?.length > 0 && (
                <Chip size="sm" color="success" variant="flat" className="text-xs font-semibold">
                  Top: {topUser}
                </Chip>
              )}
            </div>

            <Table
              aria-label="Gamer Leaderboard Table"
              className="bg-transparent"
              classNames={{
                th: "bg-white/[0.04] text-slate-400 font-semibold text-xs uppercase border-b border-white/10",
                td: "py-3 border-b border-white/5 text-sm"
              }}
            >
              <TableHeader>
                <TableColumn width={80}>RANK</TableColumn>
                <TableColumn>USER</TableColumn>
                <TableColumn>TOTAL PLAYTIME</TableColumn>
                <TableColumn align="end">SHARE</TableColumn>
              </TableHeader>
              <TableBody emptyContent="No recorded game sessions yet. Launch and play any game to begin recording playtime telemetry.">
                {(playtimeStats.users || []).map((user, index) => {
                  const sharePercent =
                    playtimeStats.totalSeconds > 0
                      ? Math.round((user.totalSeconds / playtimeStats.totalSeconds) * 100)
                      : 0;

                  return (
                    <TableRow key={user.userId}>
                      <TableCell>
                        <div className="flex items-center gap-1.5">
                          {index === 0 && <Flame className="w-4 h-4 text-warning animate-pulse" />}
                          <span
                            className={`font-bold text-sm ${
                              index === 0
                                ? 'text-warning font-mono'
                                : index === 1
                                ? 'text-slate-300 font-mono'
                                : index === 2
                                ? 'text-amber-600 font-mono'
                                : 'text-slate-500 font-mono'
                            }`}
                          >
                            #{index + 1}
                          </span>
                        </div>
                      </TableCell>
                      <TableCell>
                        <div className="flex items-center gap-2.5">
                          <div className="w-8 h-8 rounded-full bg-primary/20 border border-primary/40 flex items-center justify-center text-primary text-xs font-bold shrink-0">
                            {user.username.charAt(0).toUpperCase()}
                          </div>
                          <div>
                            <span className="font-semibold text-white block">{user.username}</span>
                            <span className="text-[10px] text-slate-400 font-mono">ID: {user.userId.substring(0, 8)}...</span>
                          </div>
                        </div>
                      </TableCell>
                      <TableCell>
                        <span className="font-mono font-bold text-slate-200">
                          {formatDuration(user.totalSeconds)}
                        </span>
                      </TableCell>
                      <TableCell>
                        <Chip
                          size="sm"
                          variant="flat"
                          color={index === 0 ? "warning" : "default"}
                          className="font-mono text-xs"
                        >
                          {sharePercent}%
                        </Chip>
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </CardBody>
        </Card>

        {/* Server Top 10 Most Played Titles */}
        <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm flex flex-col">
          <CardBody className="p-5 sm:p-6 space-y-4 flex-1 flex flex-col">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2.5">
                <div className="w-8 h-8 rounded-xl bg-emerald-500/15 border border-emerald-500/30 flex items-center justify-center text-emerald-400">
                  <Flame className="w-4 h-4" />
                </div>
                <div>
                  <h4 className="font-bold text-white text-base">Top 10 Most Played Games</h4>
                  <p className="text-xs text-slate-400">Aggregated across all users & sessions</p>
                </div>
              </div>

              {topGamesList.length > 0 && (
                <Chip size="sm" variant="flat" color="secondary" className="text-xs font-mono">
                  {topGamesList.length} {topGamesList.length === 1 ? 'title' : 'titles'}
                </Chip>
              )}
            </div>

            {(!topGamesList || topGamesList.length === 0) ? (
              <div className="flex-1 flex flex-col items-center justify-center text-center p-6 text-slate-400 text-xs bg-white/[0.02] rounded-xl border border-white/5 space-y-2">
                <Gamepad2 className="w-8 h-8 text-slate-500 opacity-40" />
                <p className="text-slate-300 font-semibold">No recorded game sessions yet</p>
                <p className="text-slate-500 text-[11px]">Play games through Jellyfin to log server playtime.</p>
              </div>
            ) : (
              <div className="space-y-2 flex-1 overflow-y-auto max-h-[380px] pr-1">
                {topGamesList.map((g, idx) => (
                  <div
                    key={g.itemId || idx}
                    className="flex items-center justify-between p-2.5 rounded-xl bg-white/[0.03] border border-white/5 hover:border-white/15 transition-all text-xs group"
                  >
                    <div className="flex items-center gap-2.5 min-w-0 pr-2">
                      <span
                        className={`font-mono font-bold text-xs shrink-0 ${
                          idx === 0
                            ? 'text-warning font-bold'
                            : idx === 1
                            ? 'text-slate-300'
                            : idx === 2
                            ? 'text-amber-600'
                            : 'text-slate-500'
                        }`}
                      >
                        #{idx + 1}
                      </span>
                      <span className="font-semibold text-white truncate group-hover:text-primary transition-colors" title={g.itemName}>
                        {g.itemName}
                      </span>
                    </div>
                    <Chip
                      size="sm"
                      variant="flat"
                      color={idx === 0 ? "warning" : "primary"}
                      className="font-mono text-xs shrink-0 font-semibold"
                    >
                      {formatDuration(g.seconds)}
                    </Chip>
                  </div>
                ))}
              </div>
            )}
          </CardBody>
        </Card>
      </div>
    </div>
  );
};
