import { useRef, useState } from "react";
import { api } from "../api/client";
import type { PendingContractChoice, Pos, SeasonRequestKind, SeasonResult, TitleKind } from "../api/types";
import { buildClipsForSeasons, type ClipData } from "../data/clips";
import { dilemmaLine } from "../data/dilemmas";
import {
  DOMESTIC_CUP_NAME,
  LEAGUE_NAME,
  buildClubName,
  buildFinalNarrative,
  continentalName,
  randomOpponentCountry,
} from "../data/flavor";
import { INJURY_LABEL, POS_LABEL, SEASON_REQUEST_BUTTON_LABEL, TITLE_LABEL } from "../data/labels";

interface DisplayedClip {
  data: ClipData;
  resolvedAccept?: boolean;
  // Roadmap §9 Bloco 3, "múltiplas propostas": índice da proposta aceita (kind
  // "contractChoice"), ou -1 quando recusou todas.
  resolvedChoiceIndex?: number;
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
  // Painel Contrato + Técnico (roadmap pós-§9): última temporada fechada (pra mostrar
  // contrato/braçadeira/bolas paradas fora do ticker) e o pedido selecionado pra
  // próxima temporada, ainda não enviado.
  const [lastSeason, setLastSeason] = useState<SeasonResult | null>(null);
  const [pendingRequest, setPendingRequest] = useState<Exclude<SeasonRequestKind, "None"> | null>(null);
  const clubNames = useRef<Record<number, string>>({});

  function clubFor(tier: number): string {
    if (!clubNames.current[tier]) clubNames.current[tier] = buildClubName(country, tier);
    return clubNames.current[tier];
  }

  function dashFrom(c: ClipData) {
    if (c.kind === "offer") return { age: c.offer.age, overall: c.offer.overall, clubTier: c.offer.clubTier };
    // PendingContractChoice não carrega o clubTier atual (as propostas têm o tier DELAS,
    // não o de origem) — mantém o último tier conhecido no dash até a escolha resolver.
    if (c.kind === "contractChoice") return { age: c.choice.age, overall: c.choice.overall, clubTier: dash?.clubTier ?? 1 };
    return { age: c.season.age, overall: c.season.overall, clubTier: c.season.clubTier };
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
    contractChoiceIndex?: number
  ) {
    const resp = await api.advance(currentToken, decision, request, contractChoiceIndex);
    const seasonClips = buildClipsForSeasons(resp.newSeasons);
    // Mutuamente exclusivos (CareerProgress.AwaitingDecision) — no máximo um dos dois
    // vem preenchido.
    const offerClip: ClipData[] = resp.pendingOffer ? [{ kind: "offer", offer: resp.pendingOffer }] : [];
    const contractClip: ClipData[] = resp.pendingContractChoice
      ? [{ kind: "contractChoice", choice: resp.pendingContractChoice }]
      : [];
    return { clips: [...seasonClips, ...offerClip, ...contractClip], finished: resp.finished, token: resp.token };
  }

  function isPause(c: ClipData): c is Extract<ClipData, { kind: "offer" | "contractChoice" }> {
    return c.kind === "offer" || c.kind === "contractChoice";
  }

  function popAndDisplay(nextQueue: ClipData[]) {
    if (nextQueue.length === 0) return;
    const [head, ...rest] = nextQueue;
    setQueue(rest);
    setDash(dashFrom(head));
    if (!isPause(head)) setLastSeason(head.season);
    setDisplayed((prev) => {
      const arr = [...prev, { data: head }];
      if (isPause(head)) setAwaitingIndex(arr.length - 1);
      return arr;
    });
  }

  // Painel Contrato + Técnico: só dá pra anexar um pedido novo quando esta chamada vai
  // de fato bater na API pedindo temporadas novas — ou seja, quando não há nada em fila
  // já buscado (senão o pedido cairia numa temporada que o motor já revelou noutra
  // chamada, fora de ordem). Casa com a mesma janela em que "Avançar" vira rede de
  // verdade em vez de só paginar localmente.
  const canRequest = queue.length === 0 && !finished && awaitingIndex === null && !loading;

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

  // Roadmap §9 Bloco 3, "múltiplas propostas": index >= 0 aceita aquela proposta;
  // -1 recusa todas (contrato curto de "prova" — mesmo padrão do motor).
  async function handleContractChoice(index: number) {
    if (awaitingIndex === null) return;
    const idx = awaitingIndex;
    setDisplayed((prev) => prev.map((d, i) => (i === idx ? { ...d, resolvedChoiceIndex: index } : d)));
    setAwaitingIndex(null);
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
        } else if (pendingQueue.length > 0 && pendingQueue[0].kind === "contractChoice") {
          const choiceClip = pendingQueue.shift()!;
          // Pulando tudo: aceita a primeira proposta de upgrade, se houver; senão recusa
          // todas (-1) — mesmo espírito de "aceitar upgrade" que o pulo já faz pra ofertas.
          const proposals = choiceClip.kind === "contractChoice" ? choiceClip.choice.proposals : [];
          const upgradeIdx = proposals.findIndex((p) => p.upgrade);
          const chosen = upgradeIdx >= 0 ? upgradeIdx : -1;
          setDisplayed((prev) => [...prev, { data: choiceClip, resolvedChoiceIndex: chosen }]);
          const more = await fetchMore(currentToken, undefined, undefined, chosen);
          currentToken = more.token;
          done = more.finished && more.clips.length === 0;
          pendingQueue = [...pendingQueue, ...more.clips];
        } else if (pendingQueue.length > 0) {
          const clip = pendingQueue.shift()!;
          setDisplayed((prev) => [...prev, { data: clip }]);
          setDash(dashFrom(clip));
          if (!isPause(clip)) setLastSeason(clip.season);
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

        {lastSeason && (
          <ManagementPanel
            lastSeason={lastSeason}
            pendingRequest={pendingRequest}
            canRequest={canRequest}
            onSelect={(k) => setPendingRequest((cur) => (cur === k ? null : k))}
          />
        )}

        <div className="ticker-wrap">
          {displayed.map((d, i) => (
            <Clip
              key={i}
              clip={d.data}
              resolvedAccept={d.resolvedAccept}
              resolvedChoiceIndex={d.resolvedChoiceIndex}
              clubFor={clubFor}
              nickname={nickname}
              country={country}
              onAccept={() => handleDecision(true)}
              onDecline={() => handleDecision(false)}
              onChooseContract={handleContractChoice}
              isAwaiting={awaitingIndex === i}
            />
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
  clip, resolvedAccept, resolvedChoiceIndex, isAwaiting, clubFor, nickname, country, onAccept, onDecline, onChooseContract,
}: {
  clip: ClipData;
  resolvedAccept?: boolean;
  resolvedChoiceIndex?: number;
  isAwaiting: boolean;
  clubFor: (tier: number) => string;
  nickname: string;
  country: string;
  onAccept: () => void;
  onDecline: () => void;
  onChooseContract: (index: number) => void;
}) {
  if (clip.kind === "final") return <FinalClip season={clip.season} title={clip.title} clubFor={clubFor} nickname={nickname} country={country} />;
  if (clip.kind === "season") return <SeasonClip season={clip.season} clubFor={clubFor} country={country} />;
  if (clip.kind === "awards") return <AwardsClip season={clip.season} />;
  if (clip.kind === "retire") return <div className="clip"><div className="season-tag">{clip.season.age} anos</div><div className="headline">Fim precoce da carreira</div><div className="body">Uma lesão grave encerra a carreira antes da hora. A torcida se despede com carinho.</div></div>;
  if (clip.kind === "contractChoice") return <ContractChoiceClip choice={clip.choice} clubFor={clubFor} isAwaiting={isAwaiting} resolvedChoiceIndex={resolvedChoiceIndex} onChoose={onChooseContract} />;
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

function SeasonClip({ season, clubFor, country }: { season: SeasonResult; clubFor: (t: number) => string; country: string }) {
  const champion = season.leaguePosition === 1;
  const club = clubFor(season.clubTier);
  const leagueName = LEAGUE_NAME[country] ?? "Liga Nacional";
  const injuryNote = INJURY_LABEL[season.injury];
  const note = moraleNote(season);
  const reqNote = requestNote(season);
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

function OfferClip({ offer, clubFor, isAwaiting, resolvedAccept, onAccept, onDecline }: {
  offer: { age: number; clubTier: number; upgrade: boolean; contractExpiring: boolean };
  clubFor: (t: number) => string;
  isAwaiting: boolean;
  resolvedAccept?: boolean;
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
      {isAwaiting ? (
        <div className="transfer-actions">
          <button className="btn-mini accept" onClick={onAccept}>Aceitar e assinar</button>
          <button className="btn-mini decline" onClick={onDecline}>{offer.contractExpiring ? "Renovar curto e provar de novo" : "Permanecer"}</button>
        </div>
      ) : (
        <div style={{ marginTop: 8, fontFamily: "var(--font-b)", fontWeight: 700, fontSize: 12, color: resolvedAccept ? "var(--blue)" : "var(--ink-soft)" }}>
          {resolvedAccept ? `✔ Assinado com ${toClub}` : "✔ Permaneceu no clube"}
        </div>
      )}
    </div>
  );
}

// Roadmap §9 Bloco 3, "múltiplas propostas": até 3 clubes concretos aparecem quando o
// contrato vence sem renovação — o jogador escolhe um ou recusa todas (contrato curto
// de "prova", mesmo desfecho de recusar a proposta única de antes desta feature).
function ContractChoiceClip({ choice, clubFor, isAwaiting, resolvedChoiceIndex, onChoose }: {
  choice: PendingContractChoice;
  clubFor: (t: number) => string;
  isAwaiting: boolean;
  resolvedChoiceIndex?: number;
  onChoose: (index: number) => void;
}) {
  return (
    <div className="clip clip-transfer">
      <div className="season-tag">Fim de contrato · {choice.age} anos</div>
      <div className="headline">Seu contrato venceu e o clube não renovou</div>
      <div className="body">Chegaram propostas de fora. Escolha uma pra assinar ou recuse todas e tente provar seu valor num contrato curto.</div>
      {isAwaiting ? (
        <div className="transfer-actions" style={{ flexDirection: "column", alignItems: "stretch" }}>
          {choice.proposals.map((p, i) => (
            <button key={i} className="btn-mini accept" onClick={() => onChoose(i)}>
              {p.upgrade ? "⬆️ " : ""}Assinar com {clubFor(p.clubTier)}
            </button>
          ))}
          <button className="btn-mini decline" onClick={() => onChoose(-1)}>Recusar todas — contrato curto de prova</button>
        </div>
      ) : (
        <div style={{ marginTop: 8, fontFamily: "var(--font-b)", fontWeight: 700, fontSize: 12, color: resolvedChoiceIndex !== undefined && resolvedChoiceIndex >= 0 ? "var(--blue)" : "var(--ink-soft)" }}>
          {resolvedChoiceIndex !== undefined && resolvedChoiceIndex >= 0
            ? `✔ Assinado com ${clubFor(choice.proposals[resolvedChoiceIndex].clubTier)}`
            : "✔ Recusou todas — contrato curto de prova"}
        </div>
      )}
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
