import { useRef, useState } from "react";
import { api } from "../api/client";
import type { Pos, SeasonResult, TitleKind } from "../api/types";
import { buildClipsForSeasons, type ClipData } from "../data/clips";
import {
  DOMESTIC_CUP_NAME,
  LEAGUE_NAME,
  buildClubName,
  buildFinalNarrative,
  continentalName,
  randomOpponentCountry,
} from "../data/flavor";
import { INJURY_LABEL, POS_LABEL, TITLE_LABEL } from "../data/labels";

interface DisplayedClip {
  data: ClipData;
  resolvedAccept?: boolean;
}

interface Props {
  nickname: string;
  country: string;
  position: Pos;
  role: string;
  potential: number;
  initialToken: string;
  onExit: () => void;
  onFinished: (token: string) => void;
}

export function Sim({ nickname, country, position, role, potential, initialToken, onExit, onFinished }: Props) {
  const [token, setToken] = useState(initialToken);
  const [queue, setQueue] = useState<ClipData[]>([]);
  const [displayed, setDisplayed] = useState<DisplayedClip[]>([]);
  const [finished, setFinished] = useState(false);
  const [awaitingIndex, setAwaitingIndex] = useState<number | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [dash, setDash] = useState<{ age: number; overall: number; clubTier: number } | null>(null);
  const clubNames = useRef<Record<number, string>>({});

  function clubFor(tier: number): string {
    if (!clubNames.current[tier]) clubNames.current[tier] = buildClubName(country, tier);
    return clubNames.current[tier];
  }

  function dashFrom(c: ClipData) {
    if (c.kind === "offer") return { age: c.offer.age, overall: c.offer.overall, clubTier: c.offer.clubTier };
    return { age: c.season.age, overall: c.season.overall, clubTier: c.season.clubTier };
  }

  // Não lê nem grava o token no estado do React — quem chama passa o token explicitamente
  // e recebe o novo de volta. Isso importa porque handleSkipAll chama isto várias vezes
  // em sequência sem esperar um re-render entre uma chamada e outra; se lesse `token` do
  // estado, todas as chamadas do laço usariam o MESMO token antigo (setState não é
  // síncrono) e cada avanço pisaria no anterior em vez de continuar dele.
  async function fetchMore(currentToken: string, decision?: boolean) {
    const resp = await api.advance(currentToken, decision);
    const seasonClips = buildClipsForSeasons(resp.newSeasons);
    const offerClip: ClipData[] = resp.pendingOffer ? [{ kind: "offer", offer: resp.pendingOffer }] : [];
    return { clips: [...seasonClips, ...offerClip], finished: resp.finished, token: resp.token };
  }

  function popAndDisplay(nextQueue: ClipData[]) {
    if (nextQueue.length === 0) return;
    const [head, ...rest] = nextQueue;
    setQueue(rest);
    setDash(dashFrom(head));
    setDisplayed((prev) => {
      const arr = [...prev, { data: head }];
      if (head.kind === "offer") setAwaitingIndex(arr.length - 1);
      return arr;
    });
  }

  async function handleAdvance() {
    if (awaitingIndex !== null || loading) return;
    setError(null);
    if (queue.length > 0) {
      popAndDisplay(queue);
      return;
    }
    if (finished) return;
    setLoading(true);
    try {
      const more = await fetchMore(token);
      setToken(more.token);
      setFinished(more.finished);
      popAndDisplay(more.clips);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Erro ao avançar a carreira.");
    } finally {
      setLoading(false);
    }
  }

  async function handleDecision(accept: boolean) {
    if (awaitingIndex === null) return;
    const idx = awaitingIndex;
    setDisplayed((prev) => prev.map((d, i) => (i === idx ? { ...d, resolvedAccept: accept } : d)));
    setAwaitingIndex(null);
    setLoading(true);
    setError(null);
    try {
      const more = await fetchMore(token, accept);
      setToken(more.token);
      setFinished(more.finished);
      setQueue((q) => [...q, ...more.clips]);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Erro ao decidir a transferência.");
    } finally {
      setLoading(false);
    }
  }

  async function handleSkipAll() {
    setLoading(true);
    setError(null);
    try {
      let pendingQueue = [...queue];
      let currentToken = token;
      let done = finished;
      let guard = 0;
      while (!done && guard < 200) {
        guard++;
        if (pendingQueue.length > 0 && pendingQueue[0].kind === "offer") {
          const offerClip = pendingQueue.shift()!;
          const accept = offerClip.kind === "offer" ? offerClip.offer.upgrade : false;
          setDisplayed((prev) => [...prev, { data: offerClip, resolvedAccept: accept }]);
          const more = await fetchMore(currentToken, accept);
          currentToken = more.token;
          done = more.finished && more.clips.length === 0;
          pendingQueue = [...pendingQueue, ...more.clips];
        } else if (pendingQueue.length > 0) {
          const clip = pendingQueue.shift()!;
          setDisplayed((prev) => [...prev, { data: clip }]);
          setDash(dashFrom(clip));
        } else {
          const more = await fetchMore(currentToken);
          currentToken = more.token;
          done = more.finished && more.clips.length === 0;
          pendingQueue = [...pendingQueue, ...more.clips];
        }
      }
      setToken(currentToken);
      setFinished(true);
      setQueue([]);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Erro ao pular a carreira.");
    } finally {
      setLoading(false);
    }
  }

  const canFinish = finished && queue.length === 0 && awaitingIndex === null;

  return (
    <section className="screen pitch-bg">
      <div className="wrap">
        <div className="top-nav">
          <button className="back-btn" onClick={onExit}>✕ Sair</button>
          <span className="step-label">{POS_LABEL[position]} · {role}</span>
        </div>
        <h2 className="h2">Recortes da <span className="accent">carreira</span></h2>

        {error && <div className="error-banner">{error}</div>}

        <div className="dash-bar">
          <div className="dash-item"><div className="dash-k">Idade</div><div className="dash-v">{dash?.age ?? "—"}</div></div>
          <div className="dash-item"><div className="dash-k">Overall</div><div className="dash-v">{dash?.overall ?? "—"}</div></div>
          <div className="dash-item"><div className="dash-k">Potencial</div><div className="dash-v">{potential}</div></div>
          <div className="dash-item"><div className="dash-k">Clube</div><div className="dash-v" style={{ fontSize: 12 }}>{dash ? clubFor(dash.clubTier) : "—"}</div></div>
        </div>

        <div className="ticker-wrap">
          {displayed.map((d, i) => (
            <Clip key={i} clip={d.data} resolvedAccept={d.resolvedAccept} clubFor={clubFor} nickname={nickname} country={country} onAccept={() => handleDecision(true)} onDecline={() => handleDecision(false)} isAwaiting={awaitingIndex === i} />
          ))}
          {displayed.length === 0 && !loading && (
            <p className="empty-msg">Clique em "Avançar" pra começar sua primeira temporada.</p>
          )}
        </div>

        <div className="sim-controls">
          {canFinish ? (
            <button className="btn rust" onClick={() => onFinished(token)}>Ver Resultado Final 🏆</button>
          ) : (
            <>
              <button className="btn rust" disabled={loading || awaitingIndex !== null} onClick={handleAdvance}>
                {loading ? "Carregando…" : awaitingIndex !== null ? "Aguardando sua decisão…" : "Avançar →"}
              </button>
              <button className="btn secondary" disabled={loading || awaitingIndex !== null} onClick={handleSkipAll}>Pular tudo ⏭</button>
            </>
          )}
        </div>
      </div>
    </section>
  );
}

function Clip({
  clip, resolvedAccept, isAwaiting, clubFor, nickname, country, onAccept, onDecline,
}: {
  clip: ClipData;
  resolvedAccept?: boolean;
  isAwaiting: boolean;
  clubFor: (tier: number) => string;
  nickname: string;
  country: string;
  onAccept: () => void;
  onDecline: () => void;
}) {
  if (clip.kind === "final") return <FinalClip season={clip.season} title={clip.title} clubFor={clubFor} nickname={nickname} country={country} />;
  if (clip.kind === "season") return <SeasonClip season={clip.season} clubFor={clubFor} country={country} />;
  if (clip.kind === "awards") return <AwardsClip season={clip.season} />;
  if (clip.kind === "retire") return <div className="clip"><div className="season-tag">{clip.season.age} anos</div><div className="headline">Fim precoce da carreira</div><div className="body">Uma lesão grave encerra a carreira antes da hora. A torcida se despede com carinho.</div></div>;
  return <OfferClip offer={clip.offer} clubFor={clubFor} isAwaiting={isAwaiting} resolvedAccept={resolvedAccept} onAccept={onAccept} onDecline={onDecline} />;
}

function FinalClip({ season, title, clubFor, nickname, country }: { season: SeasonResult; title: TitleKind; clubFor: (t: number) => string; nickname: string; country: string }) {
  const label =
    title === "DomesticCup" ? (DOMESTIC_CUP_NAME[country] ?? "Copa Nacional") :
    title === "WorldCup" ? "Final da Copa do Mundo" :
    continentalName(country, title === "ContinentalPrimary");
  const clubName = title === "WorldCup" ? `Seleção de ${country}` : clubFor(season.clubTier);
  const opponent = title === "WorldCup" ? `Seleção de ${randomOpponentCountry(country)}` : clubFor(season.clubTier + (Math.random() < 0.5 ? 1 : -1));
  const { teamGoals, oppGoals, events } = buildFinalNarrative(nickname, true);
  return (
    <div className="clip clip-final">
      <div className="live-badge"><span className="live-dot" /> AO VIVO</div>
      <div className="season-tag">Final · {label} · {season.age} anos</div>
      <div className="headline">{clubName} vs {opponent}</div>
      <div className="final-events">
        {events.map((e, i) => (
          <div className="final-event" key={i}>{e.minute}' — {e.side === "team" ? "⚽" : "🥅"} {e.who}</div>
        ))}
      </div>
      <div className="body" style={{ marginTop: 4, fontWeight: 700 }}>
        {clubName} {teamGoals} x {oppGoals} {opponent} — CAMPEÃO! 🏆
      </div>
    </div>
  );
}

// Moral (Roadmap §9 Bloco 2): ícone por faixa, -1.0..+1.0.
function moraleIcon(v: number): string {
  if (v >= 0.4) return "😄";
  if (v >= 0.1) return "🙂";
  if (v > -0.1) return "😐";
  if (v > -0.4) return "😕";
  return "😠";
}

function moraleNote(season: SeasonResult): string | null {
  if (season.askedToLeave) return "😤 O clima no vestiário azedou — você deixou claro que quer sair.";
  if (season.declinedBiggerClub) return "❤️ Recusou uma proposta de fora e a torcida vibrou com a lealdade.";
  if (season.moraleDilemma) return "🗣️ Um imprevisto nos bastidores mexeu com o humor do grupo.";
  return null;
}

function SeasonClip({ season, clubFor, country }: { season: SeasonResult; clubFor: (t: number) => string; country: string }) {
  const champion = season.leaguePosition === 1;
  const club = clubFor(season.clubTier);
  const leagueName = LEAGUE_NAME[country] ?? "Liga Nacional";
  const injuryNote = INJURY_LABEL[season.injury];
  const note = moraleNote(season);
  return (
    <div className="clip">
      <div className="season-tag">Resumo · {season.age} anos · Overall {season.overall} · {club}</div>
      <div className="headline">{champion ? "Campeão" : "Temporada"} no {club}</div>
      <div className="body">
        {injuryNote && <>{injuryNote} </>}
        {champion ? `Campeão do ${leagueName}! ` : `Terminou o ${leagueName} em ${season.leaguePosition}º lugar. `}
        {season.caps > 0 && `Chamado para defender a Seleção de ${country}. `}
      </div>
      <div className="stats-line">
        ⚽ {season.goals} gols · 🎯 {season.assists} assist. · 🛡 {season.tackles} desarmes · 🏟 {season.apps} jogos
        {season.caps > 0 && ` · 🎽 ${season.caps} pela seleção`}
      </div>
      <div className="stats-line" style={{ marginTop: 4 }}>
        {moraleIcon(season.teamMorale)} Elenco · {moraleIcon(season.coachMorale)} Técnico · {moraleIcon(season.crowdMorale)} Torcida
      </div>
      {note && <div className="body" style={{ marginTop: 4, fontStyle: "italic" }}>{note}</div>}
    </div>
  );
}

function AwardsClip({ season }: { season: SeasonResult }) {
  const lines: string[] = [];
  if (season.titles.includes("TeamOfTheYear") || season.inTeamOfTheYear) lines.push(`⭐ ${TITLE_LABEL.TeamOfTheYear} — o melhor da sua posição na temporada.`);
  if (season.titles.includes("BallonDOr")) lines.push(`🏆 BOLA DE OURO! O melhor jogador do mundo na temporada.`);
  if (season.titles.includes("SouthAmericanTeamOfTheYear")) lines.push(`⭐ ${TITLE_LABEL.SouthAmericanTeamOfTheYear} — o melhor da sua posição no continente.`);
  if (season.titles.includes("KingOfAmerica")) lines.push(`👑 REI DA AMÉRICA! O melhor jogador do continente na temporada.`);
  return (
    <div className="clip clip-award">
      <div className="season-tag">Premiação · {season.age} anos</div>
      <div className="headline">Noite de gala</div>
      <div className="body">{lines.map((l, i) => <div key={i}>{l}</div>)}</div>
    </div>
  );
}

function OfferClip({ offer, clubFor, isAwaiting, resolvedAccept, onAccept, onDecline }: {
  offer: { age: number; clubTier: number; upgrade: boolean };
  clubFor: (t: number) => string;
  isAwaiting: boolean;
  resolvedAccept?: boolean;
  onAccept: () => void;
  onDecline: () => void;
}) {
  const toClub = clubFor(offer.clubTier + (offer.upgrade ? 1 : -1));
  const label = offer.upgrade ? "Proposta de um clube maior" : "Proposta de um clube menor, mas com mais minutos em campo";
  return (
    <div className="clip clip-transfer">
      <div className="season-tag">Janela de transferências · {offer.age} anos</div>
      <div className="headline">{label}</div>
      <div className="body">{toClub} quer contar com você. Aceitar a proposta ou permanecer no clube atual?</div>
      {isAwaiting ? (
        <div className="transfer-actions">
          <button className="btn-mini accept" onClick={onAccept}>Aceitar e assinar</button>
          <button className="btn-mini decline" onClick={onDecline}>Permanecer</button>
        </div>
      ) : (
        <div style={{ marginTop: 8, fontFamily: "var(--font-b)", fontWeight: 700, fontSize: 12, color: resolvedAccept ? "var(--blue)" : "var(--ink-soft)" }}>
          {resolvedAccept ? `✔ Assinado com ${toClub}` : "✔ Permaneceu no clube"}
        </div>
      )}
    </div>
  );
}
