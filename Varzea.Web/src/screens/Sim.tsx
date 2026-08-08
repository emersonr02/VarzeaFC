import { useEffect, useRef, useState } from "react";
import { api } from "../api/client";
import type { PendingContractChoice, Pos, SeasonRequestKind, SeasonResult, TitleKind } from "../api/types";
import { buildClipsForSeasons, type ClipData } from "../data/clips";
import { dilemmaLine } from "../data/dilemmas";
import {
  DOMESTIC_CUP_NAME,
  LEAGUE_NAME,
  SECOND_DIVISION_NAME,
  buildClubName,
  buildFinalNarrative,
  continentalName,
  randomOpponentCountry,
} from "../data/flavor";
import { INJURY_LABEL, POS_LABEL, SEASON_REQUEST_BUTTON_LABEL, TITLE_LABEL } from "../data/labels";

// Ofertas/propostas de contrato SEMPRE pausam a carreira até serem decididas — nunca
// entram na timeline das rodadas, aparecem como notificação separada (ver
// PendingPauseNotification). Só existem dois tipos de pausa (mutuamente exclusivos, ver
// CareerProgress.AwaitingDecision no motor).
type PendingPause = Extract<ClipData, { kind: "offer" | "contractChoice" }>;

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
  // Só temporada/final/prêmio/aposentadoria — ofertas e propostas de contrato NUNCA
  // entram aqui (ver pendingPause abaixo), então não precisam de estado de "resolvido".
  const [displayed, setDisplayed] = useState<Exclude<ClipData, PendingPause>[]>([]);
  const [finished, setFinished] = useState(false);
  // Notificação de oferta/proposta pendente — fora da timeline das rodadas (ver
  // PendingPauseNotification). Só existe uma pausa por vez, nunca junto com a outra.
  const [pendingPause, setPendingPause] = useState<PendingPause | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [dash, setDash] = useState<{ age: number; overall: number; clubTier: number; clubName: string } | null>(null);
  // Painel Contrato + Técnico (roadmap pós-§9): última temporada fechada (pra mostrar
  // contrato/braçadeira/bolas paradas fora do ticker) e o pedido selecionado pra
  // próxima temporada, ainda não enviado.
  const [lastSeason, setLastSeason] = useState<SeasonResult | null>(null);
  const [pendingRequest, setPendingRequest] = useState<Exclude<SeasonRequestKind, "None"> | null>(null);
  const clubNames = useRef<Record<number, string>>({});
  const tickerRef = useRef<HTMLDivElement>(null);

  // Timeline de tamanho fixo (ver .ticker-wrap): desce sozinha pro final a cada recorte
  // novo, sem o jogador precisar rolar manualmente.
  useEffect(() => {
    tickerRef.current?.scrollTo({ top: tickerRef.current.scrollHeight, behavior: "smooth" });
  }, [displayed]);

  function clubFor(tier: number): string {
    if (!clubNames.current[tier]) clubNames.current[tier] = buildClubName(country, tier);
    return clubNames.current[tier];
  }

  function dashFrom(c: ClipData) {
    // Ofertas/propostas ainda não resolvidas não têm um clube de destino REAL definido
    // (isso é o próximo passo do roadmap — mais propostas de clubes) — o dash continua
    // mostrando o clube ATUAL até a decisão fechar.
    if (c.kind === "offer") return { age: c.offer.age, overall: c.offer.overall, clubTier: c.offer.clubTier, clubName: dash?.clubName ?? "" };
    if (c.kind === "contractChoice") return { age: c.choice.age, overall: c.choice.overall, clubTier: dash?.clubTier ?? 1, clubName: dash?.clubName ?? "" };
    return { age: c.season.age, overall: c.season.overall, clubTier: c.season.clubTier, clubName: c.season.clubName };
  }

  // Não lê nem grava o token no estado do React — quem chama passa o token explicitamente
  // e recebe o novo de volta. Isso importa porque handleSkipAll chama isto várias vezes
  // em sequência sem esperar um re-render entre uma chamada e outra; se lesse `token` do
  // estado, todas as chamadas do laço usariam o MESMO token antigo (setState não é
  // síncrono) e cada avanço pisaria no anterior em vez de continuar dele.
  async function fetchMore(
    currentToken: string,
    decision?: boolean,
    request?: SeasonRequestKind,
    contractChoiceIndex?: number,
    revealAll?: boolean
  ) {
    const resp = await api.advance(currentToken, decision, request, contractChoiceIndex, revealAll);
    const seasonClips = buildClipsForSeasons(resp.newSeasons);
    // Mutuamente exclusivos (CareerProgress.AwaitingDecision) — no máximo um dos dois
    // vem preenchido.
    const offerClip: ClipData[] = resp.pendingOffer ? [{ kind: "offer", offer: resp.pendingOffer }] : [];
    const contractClip: ClipData[] = resp.pendingContractChoice
      ? [{ kind: "contractChoice", choice: resp.pendingContractChoice }]
      : [];
    return { clips: [...seasonClips, ...offerClip, ...contractClip], finished: resp.finished, token: resp.token };
  }

  function isPause(c: ClipData): c is PendingPause {
    return c.kind === "offer" || c.kind === "contractChoice";
  }

  // Ofertas/propostas nunca entram na timeline (ver PendingPauseNotification) — só
  // pausam a fila local até serem decididas, fora do fluxo normal de "Avançar".
  function popAndDisplay(nextQueue: ClipData[]) {
    if (nextQueue.length === 0) return;
    const [head, ...rest] = nextQueue;
    setQueue(rest);
    setDash(dashFrom(head));
    if (isPause(head)) {
      setPendingPause(head);
    } else {
      setLastSeason(head.season);
      setDisplayed((prev) => [...prev, head]);
    }
  }

  // Painel Contrato + Técnico: só dá pra anexar um pedido novo quando esta chamada vai
  // de fato bater na API pedindo temporadas novas — ou seja, quando não há nada em fila
  // já buscado (senão o pedido cairia numa temporada que o motor já revelou noutra
  // chamada, fora de ordem). Casa com a mesma janela em que "Avançar" vira rede de
  // verdade em vez de só paginar localmente.
  const canRequest = queue.length === 0 && !finished && pendingPause === null && !loading;

  async function handleAdvance() {
    if (pendingPause !== null || loading) return;
    setError(null);
    if (queue.length > 0) {
      popAndDisplay(queue);
      return;
    }
    if (finished) return;
    setLoading(true);
    try {
      const more = await fetchMore(token, undefined, pendingRequest ?? undefined);
      setToken(more.token);
      setFinished(more.finished);
      popAndDisplay(more.clips);
      setPendingRequest(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Erro ao avançar a carreira.");
    } finally {
      setLoading(false);
    }
  }

  async function handleDecision(accept: boolean) {
    if (pendingPause === null) return;
    setPendingPause(null);
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

  // Roadmap §9 Bloco 3, "múltiplas propostas": index >= 0 aceita aquela proposta;
  // -1 recusa todas (contrato curto de "prova" — mesmo padrão do motor).
  async function handleContractChoice(index: number) {
    if (pendingPause === null) return;
    setPendingPause(null);
    setLoading(true);
    setError(null);
    try {
      const more = await fetchMore(token, undefined, undefined, index);
      setToken(more.token);
      setFinished(more.finished);
      setQueue((q) => [...q, ...more.clips]);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Erro ao decidir a proposta de contrato.");
    } finally {
      setLoading(false);
    }
  }

  async function handleSkipAll() {
    setLoading(true);
    setError(null);
    setPendingRequest(null); // Pular tudo não faz pedidos do painel Contrato + Técnico
    setPendingPause(null); // Pulando tudo, qualquer pausa em tela é resolvida direto abaixo
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
          const more = await fetchMore(currentToken, accept, undefined, undefined, true);
          currentToken = more.token;
          done = more.finished && more.clips.length === 0;
          pendingQueue = [...pendingQueue, ...more.clips];
        } else if (pendingQueue.length > 0 && pendingQueue[0].kind === "contractChoice") {
          const choiceClip = pendingQueue.shift()!;
          // Pulando tudo: aceita a primeira proposta de upgrade, se houver; senão recusa
          // todas (-1) — mesmo espírito de "aceitar upgrade" que o pulo já faz pra ofertas.
          const proposals = choiceClip.kind === "contractChoice" ? choiceClip.choice.proposals : [];
          const upgradeIdx = proposals.findIndex((p) => p.upgrade);
          const chosen = upgradeIdx >= 0 ? upgradeIdx : -1;
          const more = await fetchMore(currentToken, undefined, undefined, chosen, true);
          currentToken = more.token;
          done = more.finished && more.clips.length === 0;
          pendingQueue = [...pendingQueue, ...more.clips];
        } else if (pendingQueue.length > 0) {
          const clip = pendingQueue.shift()!;
          if (!isPause(clip)) {
            setDisplayed((prev) => [...prev, clip]);
            setLastSeason(clip.season);
          }
          setDash(dashFrom(clip));
        } else {
          const more = await fetchMore(currentToken, undefined, undefined, undefined, true);
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

  const canFinish = finished && queue.length === 0 && pendingPause === null;

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
          <div className="dash-item"><div className="dash-k">Clube</div><div className="dash-v" style={{ fontSize: 12 }}>{dash?.clubName || "—"}</div></div>
        </div>

        {lastSeason && (
          <ManagementPanel
            lastSeason={lastSeason}
            pendingRequest={pendingRequest}
            canRequest={canRequest}
            onSelect={(k) => setPendingRequest((cur) => (cur === k ? null : k))}
          />
        )}

        {/* Ofertas/propostas são notificação FORA da timeline das rodadas — nunca
            entram no ticker abaixo (ver pendingPause/popAndDisplay). */}
        {pendingPause && (
          <PendingPauseNotification
            pause={pendingPause}
            clubFor={clubFor}
            onAccept={() => handleDecision(true)}
            onDecline={() => handleDecision(false)}
            onChooseContract={handleContractChoice}
          />
        )}

        <div className="ticker-wrap" ref={tickerRef}>
          {displayed.map((clip, i) => (
            <Clip key={i} clip={clip} nickname={nickname} country={country} clubFor={clubFor} />
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
              <button className="btn rust" disabled={loading || pendingPause !== null} onClick={handleAdvance}>
                {loading ? "Carregando…" : pendingPause !== null ? "Aguardando sua decisão…" : "Avançar →"}
              </button>
              <button className="btn secondary" disabled={loading || pendingPause !== null} onClick={handleSkipAll}>Pular tudo ⏭</button>
            </>
          )}
        </div>
      </div>
    </section>
  );
}

// Só temporada/final/prêmio/aposentadoria passam por aqui — ofertas e propostas de
// contrato nunca entram na timeline (ver PendingPauseNotification, fora do ticker).
function Clip({ clip, clubFor, nickname, country }: {
  clip: Exclude<ClipData, PendingPause>;
  clubFor: (tier: number) => string;
  nickname: string;
  country: string;
}) {
  if (clip.kind === "final") return <FinalClip season={clip.season} title={clip.title} clubFor={clubFor} nickname={nickname} country={country} />;
  if (clip.kind === "season") return <SeasonClip season={clip.season} country={country} />;
  if (clip.kind === "awards") return <AwardsClip season={clip.season} />;
  return <div className="clip"><div className="season-tag">{clip.season.age} anos</div><div className="headline">Fim precoce da carreira</div><div className="body">Uma lesão grave encerra a carreira antes da hora. A torcida se despede com carinho.</div></div>;
}

// Notificação fora da timeline das rodadas (ver <when_to_verify> "ofertas de
// transferência... fora da timeline"): oferta e proposta de contrato são a MESMA
// pausa lógica no motor (mutuamente exclusivas), só a UI de dentro muda.
function PendingPauseNotification({ pause, clubFor, onAccept, onDecline, onChooseContract }: {
  pause: PendingPause;
  clubFor: (tier: number) => string;
  onAccept: () => void;
  onDecline: () => void;
  onChooseContract: (index: number) => void;
}) {
  return (
    <div className="notification-banner">
      {pause.kind === "offer"
        ? <OfferClip offer={pause.offer} clubFor={clubFor} onAccept={onAccept} onDecline={onDecline} />
        : <ContractChoiceClip choice={pause.choice} clubFor={clubFor} onChoose={onChooseContract} />}
    </div>
  );
}

function FinalClip({ season, title, clubFor, nickname, country }: { season: SeasonResult; title: TitleKind; clubFor: (t: number) => string; nickname: string; country: string }) {
  const label =
    title === "DomesticCup" ? (DOMESTIC_CUP_NAME[country] ?? "Copa Nacional") :
    title === "WorldCup" ? "Final da Copa do Mundo" :
    continentalName(country, title === "ContinentalPrimary");
  const clubName = title === "WorldCup" ? `Seleção de ${country}` : season.clubName;
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
  // Conteúdo variado dos dilemas (Roadmap §9 Bloco 2, corte de escopo fechado) — antes
  // era uma mensagem genérica única.
  if (season.moraleDilemma && season.dilemmaTarget !== "None") {
    return dilemmaLine(season.dilemmaTarget, season.dilemmaPositive, season.dilemmaVariant);
  }
  return null;
}

// Painel Contrato + Técnico (roadmap pós-§9): narra o resultado do pedido feito antes
// desta temporada, se houve algum.
function requestNote(season: SeasonResult): string | null {
  switch (season.requestMade) {
    case "None": return null;
    case "RequestLeaveAtContractEnd": return "📣 Avisou o clube: vai embora quando o contrato acabar.";
    case "RequestLeaveNow": return "🚪 Pediu pra sair JÁ — o empresário saiu correndo atrás de propostas.";
    case "RequestRenewal": return season.requestGranted ? "📝 Pediu renovação antecipada — aceita!" : "📝 Pediu renovação antecipada — recusada.";
    case "RequestRaise": return season.requestGranted ? "💰 Pediu aumento — aceito!" : "💰 Pediu aumento — negado.";
    case "RequestCaptaincy": return season.requestGranted ? "🎖️ Pediu a braçadeira — agora é o capitão!" : "🎖️ Pediu a braçadeira — não rolou desta vez.";
    case "RequestSetPieces": return season.requestGranted ? "🎯 Pediu bolas paradas — concedido!" : "🎯 Pediu bolas paradas — negado.";
    case "RequestLoan": return season.requestGranted ? "🔄 Pediu empréstimo — foi pra outro clube por esta temporada." : "🔄 Pediu empréstimo — o clube preferiu manter você.";
    case "RequestRest": return "😴 Pediu descanso — jogou menos, mas recuperou fôlego.";
    case "RequestPlayInjured": return season.requestGranted ? "🩹 Insistiu em jogar lesionado." : "🩹 Estava disposto a jogar lesionado, mas não precisou.";
    case "RequestPersonalTrainer": return season.requestGranted ? "💪 Contratou um personal trainer — vai cansar menos daqui pra frente." : "💪 Já tinha um personal trainer.";
    case "RequestPromiseTitle": return season.promiseFulfilled ? "🏆 Prometeu o título pra torcida — e cumpriu!" : "😬 Prometeu o título pra torcida — e não entregou.";
    default: return null;
  }
}

// Tabela real (Roadmap pós-§9, painel Clube) — completa, não só top 5 + linha do
// jogador (pedido explícito: "mostra a tabela completa"). PROMOTION_SPOTS/
// RELEGATION_SPOTS espelham as constantes do motor (CareerSimulator.PromotionSpots/
// RelegationSpots) só pra desenhar as zonas de acesso/rebaixamento — cosmético, o
// motor já decidiu de verdade quem sobe/desce.
const PROMOTION_SPOTS = 2;
const RELEGATION_SPOTS = 4;

function leagueTableRowsToShow(season: SeasonResult): { rank: number; clubName: string; points: number; isPlayerClub: boolean }[] {
  return season.leagueTable.map((r, i) => ({ rank: i + 1, ...r }));
}

// Notícia de fim de temporada estilo "pushup" (pedido explícito, referência visual de
// outro app): manchete curta com ícone + cor, no topo do resumo de cada temporada —
// prioriza o evento mais importante (título raro > título de liga > acesso >
// rebaixamento), como uma notificação de última hora de verdade.
const TITLE_PRIORITY: TitleKind[] = [
  "BallonDOr", "KingOfAmerica", "WorldCup", "TeamOfTheYear", "SouthAmericanTeamOfTheYear",
  "ContinentalPrimary", "ContinentalSecondary", "LeagueTop5", "LeagueMid", "LeagueMinor", "DomesticCup",
];

function seasonNewsFlash(season: SeasonResult): { icon: string; color: string; headline: string } {
  const bestTitle = TITLE_PRIORITY.find((t) => season.titles.includes(t));
  if (bestTitle) return { icon: "🏆", color: "var(--gold)", headline: `${TITLE_LABEL[bestTitle].toUpperCase()}!` };
  if (season.promoted) return { icon: "⬆️", color: "#27ae60", headline: "ACESSO! O CLUBE SUBIU DE DIVISÃO" };
  if (season.relegated) return { icon: "⬇️", color: "#c0392b", headline: "REBAIXAMENTO" };
  return { icon: "📋", color: "var(--blue)", headline: "TEMPORADA ENCERRADA" };
}

function SeasonNewsFlash({ season }: { season: SeasonResult }) {
  const { icon, color, headline } = seasonNewsFlash(season);
  return (
    <div style={{
      display: "flex", alignItems: "center", gap: 10, marginBottom: 8,
      padding: "8px 10px", borderRadius: 6, background: "rgba(0,0,0,0.25)", border: `1px solid ${color}`,
    }}>
      <div style={{
        width: 30, height: 30, minWidth: 30, borderRadius: 6, background: color,
        display: "flex", alignItems: "center", justifyContent: "center", fontSize: 15,
      }}>{icon}</div>
      <div>
        <div style={{ fontSize: 9, textTransform: "uppercase", letterSpacing: "0.1em", opacity: 0.65, fontWeight: 700 }}>
          Última hora · {season.age} anos
        </div>
        <div style={{ fontFamily: "var(--font-d)", fontSize: 15, fontWeight: 700, lineHeight: 1.1 }}>{headline}</div>
      </div>
    </div>
  );
}

function SeasonClip({ season, country }: { season: SeasonResult; country: string }) {
  const champion = season.leaguePosition === 1;
  const club = season.clubName;
  const tableRows = leagueTableRowsToShow(season);
  // ClubDirectory.LeagueRivals: tier>=3 é a 1ª divisão, tier<3 é a 2ª — mesmo corte
  // usado no motor (ver ClubDirectory.cs). Sem isto, a 2ª divisão aparecia com o nome
  // da 1ª (bug real: "SpVgg em 5º na Bundesliga" — era a 2. Bundesliga, simulada certa,
  // só rotulada errado).
  const leagueName = season.clubTier >= 3
    ? (LEAGUE_NAME[country] ?? "Liga Nacional")
    : (SECOND_DIVISION_NAME[country] ?? "Segunda Divisão");
  const injuryNote = INJURY_LABEL[season.injury];
  const note = moraleNote(season);
  const reqNote = requestNote(season);
  return (
    <div className="clip">
      <SeasonNewsFlash season={season} />
      <div className="season-tag">Resumo · {season.age} anos · Overall {season.overall} · {club}</div>
      <div className="headline">{champion ? "Campeão" : "Temporada"} no {club}</div>
      <div className="body">
        {season.recoveringFromInjury && <>🩹 Ainda sentindo os efeitos da lesão grave da temporada passada — ritmo abaixo do normal. </>}
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
      {tableRows.length > 0 && (
        <div className="body" style={{ marginTop: 6, fontSize: 11 }}>
          <div style={{ fontWeight: 700, marginBottom: 2 }}>Tabela completa · {leagueName}</div>
          <div style={{ maxHeight: 220, overflowY: "auto" }}>
            {tableRows.map((r) => {
              const relegationCut = tableRows.length - RELEGATION_SPOTS;
              const inPromotionZone = r.rank <= PROMOTION_SPOTS;
              const inRelegationZone = r.rank > relegationCut;
              return (
                <div key={r.clubName}>
                  {r.rank === relegationCut + 1 && (
                    <div style={{ borderTop: "1px dashed #c0392b", color: "#c0392b", fontSize: 9, textTransform: "uppercase", letterSpacing: "0.06em", padding: "3px 0 2px", fontWeight: 700 }}>
                      Zona de rebaixamento
                    </div>
                  )}
                  <div style={{
                    display: "flex", justifyContent: "space-between",
                    borderLeft: `3px solid ${inPromotionZone ? "#27ae60" : inRelegationZone ? "#c0392b" : "transparent"}`,
                    paddingLeft: 4,
                    fontWeight: r.isPlayerClub ? 700 : 400,
                    color: r.isPlayerClub ? "var(--blue)" : undefined,
                  }}>
                    <span>{r.rank}º {r.clubName}</span><span>{r.points} pts</span>
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}
      {note && <div className="body" style={{ marginTop: 4, fontStyle: "italic" }}>{note}</div>}
      {season.contractExpiring && season.contractRenewed && (
        <div className="body" style={{ marginTop: 4, fontStyle: "italic" }}>
          📝 Contrato renovado com {club} — o clube quer contar com você por mais alguns anos.
        </div>
      )}
      {reqNote && <div className="body" style={{ marginTop: 4, fontStyle: "italic" }}>{reqNote}</div>}
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

// Só renderiza enquanto pendente — a notificação some assim que decidida (ver
// handleDecision), não tem mais um estado "resolvido" pra mostrar aqui.
function OfferClip({ offer, clubFor, onAccept, onDecline }: {
  offer: { age: number; clubTier: number; upgrade: boolean; contractExpiring: boolean };
  clubFor: (t: number) => string;
  onAccept: () => void;
  onDecline: () => void;
}) {
  const toClub = clubFor(offer.clubTier + (offer.upgrade ? 1 : -1));
  // Roadmap §9 Bloco 3: contrato vencido sem renovação tem uma narrativa diferente de
  // uma oferta de fora do ciclo — o clube atual optou por não seguir com você.
  const label = offer.contractExpiring
    ? "Seu contrato venceu e o clube não renovou"
    : offer.upgrade
      ? "Proposta de um clube maior"
      : "Proposta de um clube menor, mas com mais minutos em campo";
  const body = offer.contractExpiring
    ? `Sem acordo pra continuar, mas ${toClub} apareceu com proposta. Assinar ou apostar numa renovação curta pra provar de novo?`
    : `${toClub} quer contar com você. Aceitar a proposta ou permanecer no clube atual?`;
  return (
    <div className="clip clip-transfer">
      <div className="season-tag">Janela de transferências · {offer.age} anos</div>
      <div className="headline">{label}</div>
      <div className="body">{body}</div>
      <div className="transfer-actions">
        <button className="btn-mini accept" onClick={onAccept}>Aceitar e assinar</button>
        <button className="btn-mini decline" onClick={onDecline}>{offer.contractExpiring ? "Renovar curto e provar de novo" : "Permanecer"}</button>
      </div>
    </div>
  );
}

// Roadmap §9 Bloco 3, "múltiplas propostas": até 3 clubes concretos aparecem quando o
// contrato vence sem renovação — o jogador escolhe um ou recusa todas (contrato curto
// de "prova", mesmo desfecho de recusar a proposta única de antes desta feature). Só
// renderiza enquanto pendente, mesmo espírito de OfferClip acima.
function ContractChoiceClip({ choice, clubFor, onChoose }: {
  choice: PendingContractChoice;
  clubFor: (t: number) => string;
  onChoose: (index: number) => void;
}) {
  const tag = choice.contractExpiring ? `Fim de contrato · ${choice.age} anos` : `Sondagem de mercado · ${choice.age} anos`;
  const headline = choice.contractExpiring ? "Seu contrato venceu e o clube não renovou" : "Outros clubes vieram te sondar";
  const body = choice.contractExpiring
    ? "Chegaram propostas de fora. Escolha uma pra assinar ou recuse todas e tente provar seu valor num contrato curto."
    : "Você ainda está sob contrato, mas o interesse é real. Escolha uma proposta pra sair agora ou recuse todas e siga onde está.";
  const declineLabel = choice.contractExpiring ? "Recusar todas — contrato curto de prova" : "Recusar todas — seguir no clube atual";
  return (
    <div className="clip clip-transfer">
      <div className="season-tag">{tag}</div>
      <div className="headline">{headline}</div>
      <div className="body">{body}</div>
      <div className="transfer-actions" style={{ flexDirection: "column", alignItems: "stretch" }}>
        {choice.proposals.map((p, i) => (
          <button key={i} className="btn-mini accept" onClick={() => onChoose(i)}>
            {p.upgrade ? "⬆️ " : ""}Assinar com {clubFor(p.clubTier)}
          </button>
        ))}
        <button className="btn-mini decline" onClick={() => onChoose(-1)}>{declineLabel}</button>
      </div>
    </div>
  );
}

type PanelKind = "contrato" | "tecnico" | "empresario" | "clube";

// Quais pedidos pertencem a cada categoria — usado só pra marcar o botão da categoria
// com "✓" quando há um pedido pendente escondido ali dentro (painel fechado).
const PANEL_REQUESTS: Record<PanelKind, SeasonRequestKind[]> = {
  contrato: ["RequestRenewal", "RequestLeaveAtContractEnd", "RequestLeaveNow", "RequestRaise"],
  tecnico: ["RequestCaptaincy", "RequestSetPieces"],
  empresario: ["RequestLoan"],
  clube: ["RequestPromiseTitle"],
};

// Painel Contrato + Técnico + Empresário + Clube (roadmap pós-§9). Cada categoria é um
// botão que abre/fecha seu conteúdo (não tudo exposto de uma vez) — dentro, o estado
// atual e UM pedido selecionável pra próxima temporada, enviado junto do próximo clique
// real em "Avançar" (ver canRequest/handleAdvance). Braçadeira e bolas paradas somem
// depois de concedidas (ficam pra sempre, não tem por que pedir de novo). Saúde/fadiga
// não aparece aqui — no modo temporada-a-temporada só lesão é visível pro jogador (a
// fadiga continua existindo no motor, só não é exposta na UI).
function ManagementPanel({ lastSeason, pendingRequest, canRequest, onSelect }: {
  lastSeason: SeasonResult;
  pendingRequest: Exclude<SeasonRequestKind, "None"> | null;
  canRequest: boolean;
  onSelect: (kind: Exclude<SeasonRequestKind, "None">) => void;
}) {
  const [openPanel, setOpenPanel] = useState<PanelKind | null>(null);
  const toggle = (p: PanelKind) => setOpenPanel((cur) => (cur === p ? null : p));
  const hasPendingIn = (p: PanelKind) => pendingRequest !== null && PANEL_REQUESTS[p].includes(pendingRequest);

  return (
    <div className="dash-bar" style={{ display: "flex", flexDirection: "column", alignItems: "stretch", gap: 10, marginTop: -6 }}>
      <div style={{ display: "flex", flexDirection: "row", gap: 6, flexWrap: "wrap" }}>
        <PanelToggleButton label="📄 Contrato" active={openPanel === "contrato"} pending={hasPendingIn("contrato")} onClick={() => toggle("contrato")} />
        <PanelToggleButton label="🎯 Técnico" active={openPanel === "tecnico"} pending={hasPendingIn("tecnico")} onClick={() => toggle("tecnico")} />
        <PanelToggleButton label="💼 Empresário" active={openPanel === "empresario"} pending={hasPendingIn("empresario")} onClick={() => toggle("empresario")} />
        <PanelToggleButton label="🏟 Clube" active={openPanel === "clube"} pending={hasPendingIn("clube")} onClick={() => toggle("clube")} />
      </div>

      {openPanel === "contrato" && (
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 8 }}>
          <div>
            <div className="dash-k">Contrato</div>
            <div className="dash-v" style={{ fontSize: 14 }}>
              {lastSeason.contractYearsRemaining} temporada{lastSeason.contractYearsRemaining === 1 ? "" : "s"} restante{lastSeason.contractYearsRemaining === 1 ? "" : "s"}
            </div>
          </div>
          <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
            <RequestButton kind="RequestRenewal" pendingRequest={pendingRequest} disabled={!canRequest} onSelect={onSelect} />
            <RequestButton kind="RequestLeaveAtContractEnd" pendingRequest={pendingRequest} disabled={!canRequest} onSelect={onSelect} />
            <RequestButton kind="RequestLeaveNow" pendingRequest={pendingRequest} disabled={!canRequest} onSelect={onSelect} />
            <RequestButton kind="RequestRaise" pendingRequest={pendingRequest} disabled={!canRequest} onSelect={onSelect} />
          </div>
        </div>
      )}

      {openPanel === "tecnico" && (
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 8 }}>
          <div>
            <div className="dash-k">Técnico</div>
            <div className="dash-v" style={{ fontSize: 14 }}>{moraleIcon(lastSeason.coachMorale)} Relação</div>
          </div>
          <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
            <RequestButton kind="RequestCaptaincy" pendingRequest={pendingRequest} disabled={!canRequest} granted={lastSeason.isCaptain} onSelect={onSelect} />
            <RequestButton kind="RequestSetPieces" pendingRequest={pendingRequest} disabled={!canRequest} granted={lastSeason.hasSetPieces} onSelect={onSelect} />
          </div>
        </div>
      )}

      {openPanel === "empresario" && (
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 8 }}>
          <div>
            <div className="dash-k">Empresário</div>
            <div className="dash-v" style={{ fontSize: 14 }}>{lastSeason.onLoan ? "🔄 Emprestado" : "Sem empréstimo"}</div>
          </div>
          <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
            <RequestButton kind="RequestLoan" pendingRequest={pendingRequest} disabled={!canRequest} onSelect={onSelect} />
          </div>
        </div>
      )}

      {openPanel === "clube" && (
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 8 }}>
          <div>
            <div className="dash-k">Clube</div>
            <div className="dash-v" style={{ fontSize: 14 }}>
              {lastSeason.promisedTitle ? (lastSeason.promiseFulfilled ? "🏆 Promessa cumprida" : "😬 Promessa quebrada") : "Sem promessa"}
            </div>
          </div>
          <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
            <RequestButton kind="RequestPromiseTitle" pendingRequest={pendingRequest} disabled={!canRequest} onSelect={onSelect} />
          </div>
        </div>
      )}

      {pendingRequest && (
        <div className="body" style={{ fontSize: 12, fontStyle: "italic" }}>
          Pedido selecionado pra próxima temporada: {SEASON_REQUEST_BUTTON_LABEL[pendingRequest]} — clique em "Avançar" pra enviar.
        </div>
      )}
    </div>
  );
}

function PanelToggleButton({ label, active, pending, onClick }: {
  label: string;
  active: boolean;
  pending: boolean;
  onClick: () => void;
}) {
  return (
    <button type="button" className={`btn-mini ${active ? "accept" : "decline"}`} onClick={onClick}>
      {label}{pending ? " ✓" : ""}
    </button>
  );
}

function RequestButton({ kind, pendingRequest, disabled, granted, onSelect }: {
  kind: Exclude<SeasonRequestKind, "None">;
  pendingRequest: Exclude<SeasonRequestKind, "None"> | null;
  disabled: boolean;
  granted?: boolean;
  onSelect: (kind: Exclude<SeasonRequestKind, "None">) => void;
}) {
  if (granted) {
    return <span className="btn-mini" style={{ opacity: 0.6, cursor: "default" }}>✓ {SEASON_REQUEST_BUTTON_LABEL[kind]}</span>;
  }
  const selected = pendingRequest === kind;
  return (
    <button
      type="button"
      className={`btn-mini ${selected ? "accept" : "decline"}`}
      disabled={disabled}
      onClick={() => onSelect(kind)}
    >
      {selected ? "✓ " : ""}{SEASON_REQUEST_BUTTON_LABEL[kind]}
    </button>
  );
}
