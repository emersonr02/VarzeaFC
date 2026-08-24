// Espelha Varzea.Api/Contracts.cs e Varzea.Engine/Model/Domain.cs. Mantido manualmente —
// se a API mudar um contrato, este arquivo precisa acompanhar (não há geração automática
// ainda; um gerador de cliente a partir do OpenAPI seria o próximo passo natural).

export type Pos = "GK" | "CB" | "FB" | "DM" | "CM" | "AM" | "W" | "SS" | "ST";
export type Attr = "Pac" | "Sho" | "Pas" | "Dri" | "Def" | "Phy" | "Ski" | "Wea";
export type InjurySeverity = "None" | "Minor" | "Moderate" | "Severe" | "CareerEnding";
export type TitleKind =
  | "LeagueTop5" | "LeagueMid" | "LeagueMinor" | "DomesticCup"
  | "ContinentalSecondary" | "ContinentalPrimary" | "WorldCup"
  | "BallonDOr" | "TeamOfTheYear"
  | "SouthAmericanTeamOfTheYear" | "KingOfAmerica";

// Painel Contrato + Técnico (roadmap pós-§9) — um pedido por temporada.
export type SeasonRequestKind =
  | "None" | "RequestRenewal" | "RequestLeaveAtContractEnd" | "RequestRaise"
  | "RequestCaptaincy" | "RequestSetPieces" | "RequestLeaveNow" | "RequestLoan"
  | "RequestRest" | "RequestPlayInjured" | "RequestPersonalTrainer" | "RequestPromiseTitle";

export type DilemmaTarget = "None" | "Team" | "Coach" | "Crowd";

export interface MetaResponse {
  rulesetVersion: string;
  countries: string[];
}

export interface LegendOption {
  name: string;
  rating: number;
}

export interface DraftRoundResponse {
  token: string;
  round: number;
  attribute: Attr;
  candidates: LegendOption[];
}

export interface PositionPotential {
  position: Pos;
  potential: number;
}

export interface DraftCompleteResponse {
  token: string;
  attributes: number[];
  potentials: PositionPotential[];
}

export interface PositionLockedResponse {
  token: string;
  potential: number;
  role: string;
}

// Roadmap pós-§9, painel Clube: uma linha da tabela de classificação real.
export interface LeagueTableRow {
  clubName: string;
  points: number;
  isPlayerClub: boolean;
}

// Modo "jogo a jogo": uma partida do campeonato nacional. Os resultados vêm da MESMA
// simulação de pontos corridos que monta a tabela, então placar e classificação nunca
// se contradizem (ver CareerSimulator.BuildMatches).
export interface MatchResult {
  round: number;
  opponent: string;
  home: boolean;
  goalsFor: number;
  goalsAgainst: number;
  playerGoals: number;
  playerAssists: number;
  played: boolean;
  rating: number;
}

export interface SeasonResult {
  age: number;
  overall: number;
  clubTier: number;
  // Partidas rodada a rodada (modo "jogo a jogo") — vazio se a carreira rodou sem
  // detalhamento. seasonRating é a nota média das partidas em que jogou.
  matches: MatchResult[];
  seasonRating: number;
  // Valor de mercado estimado em milhões de euros — número de vitrine, não entra no placar.
  marketValue: number;
  // Nome do clube real (Roadmap pós-§9) — vem de clubs.json, nunca gerado no front.
  clubName: string;
  // País do CLUBE nesta temporada (roadmap pós-§9, transferência internacional) — NUNCA
  // a nacionalidade do jogador (essa é fixa, escolhida no Setup, e não vem por temporada).
  clubCountry: string;
  apps: number;
  goals: number;
  assists: number;
  tackles: number;
  cleanSheets: number;
  leaguePosition: number;
  injury: InjurySeverity;
  titles: TitleKind[];
  caps: number;
  inTeamOfTheYear: boolean;
  hadTransferOffer: boolean;
  acceptedTransfer: boolean;
  // Moral (Roadmap §9 Bloco 2) — três valores separados, -1.0..+1.0, ao fim da temporada.
  teamMorale: number;
  coachMorale: number;
  crowdMorale: number;
  declinedBiggerClub: boolean;
  moraleDilemma: boolean;
  askedToLeave: boolean;
  dilemmaTarget: DilemmaTarget;
  dilemmaPositive: boolean;
  dilemmaVariant: number;
  // Contrato (Roadmap §9 Bloco 3)
  contractExpiring: boolean;
  contractRenewed: boolean;
  // Painel Contrato + Técnico (roadmap pós-§9)
  contractYearsRemaining: number;
  isCaptain: boolean;
  hasSetPieces: boolean;
  // Painel Empresário (roadmap pós-§9): temporada jogada emprestado, um tier abaixo,
  // sempre por UMA temporada — clubTier já reflete o clube emprestado.
  onLoan: boolean;
  // Painel Saúde (roadmap pós-§9): fadiga acumulada ao fim desta temporada (sistema
  // sempre ativo, não depende de nenhum pedido) e se tem personal trainer (permanente).
  fatigue: number;
  hasPersonalTrainer: boolean;
  // Painel Clube (roadmap pós-§9): promessa de campeonato feita antes desta temporada,
  // e se foi cumprida — promisedTitle=false quando nenhuma promessa foi feita.
  promisedTitle: boolean;
  promiseFulfilled: boolean;
  // Tabela de classificação real da divisão do jogador nesta temporada.
  leagueTable: LeagueTableRow[];
  requestMade: SeasonRequestKind;
  requestGranted: boolean;
  // "Mais eventos como lesões que influenciam a carreira real" (roadmap pós-§9): true
  // quando esta temporada carrega a ressaca de uma lesão Severe da ANTERIOR — não a
  // lesão desta temporada em si (ver injury pra isso).
  recoveringFromInjury: boolean;
  // Acesso/rebaixamento (roadmap pós-§9): true na temporada que TERMINOU dentro da
  // zona — o clube/tier só muda de fato na temporada seguinte (mesmo delay de 1
  // temporada de qualquer outra mudança de clube).
  promoted: boolean;
  relegated: boolean;
}

export interface ClubOptionsResponse {
  token: string;
  options: string[];
}

export interface ClubChosenResponse {
  token: string;
  clubName: string;
}

export interface PendingTransferOffer {
  age: number;
  overall: number;
  clubTier: number;
  upgrade: boolean;
  goals: number;
  assists: number;
  tackles: number;
  cleanSheets: number;
  leaguePosition: number;
  // Roadmap §9 Bloco 3: veio de um contrato vencido sem renovação, não de fora do ciclo.
  contractExpiring: boolean;
}

// Roadmap §9 Bloco 3, "múltiplas propostas": aparece quando o contrato vence sem
// renovação, em vez de PendingTransferOffer. ClubTier é absoluto (não relativo ao tier
// atual, ao contrário de PendingTransferOffer.upgrade).
export interface ContractProposalOption {
  clubTier: number;
  upgrade: boolean;
  // Clube real sorteado na hora de GERAR a proposta (mesmo nome que vira o clube
  // aplicado se aceita) — bug real corrigido: esse campo já existia no motor (C#) mas
  // nunca tinha sido espelhado aqui, então o front continuava usando clubFor() (nome
  // 100% inventado) pra mostrar o botão da proposta.
  clubName: string;
  // País do clube da proposta (roadmap pós-§9, transferência internacional) — quase
  // sempre o país onde o jogador já está; raramente outro (a "grande" oferta de fora).
  country: string;
}

export interface PendingContractChoice {
  age: number;
  overall: number;
  proposals: ContractProposalOption[];
  // Roadmap pós-§9, "propostas de mais clubes": agora também aparece fora do ciclo de
  // contrato (scouting/desempenho/moral) — false nesses casos, true só quando o
  // contrato realmente venceu sem renovação.
  contractExpiring: boolean;
}

export interface AdvanceResponse {
  token: string;
  newSeasons: SeasonResult[];
  pendingOffer: PendingTransferOffer | null;
  pendingContractChoice: PendingContractChoice | null;
  finished: boolean;
}

export interface ScoreBreakdown {
  titles: number;
  awards: number;
  production: number;
  peak: number;
  total: number;
}

export interface CareerTotals {
  peakOverall: number;
  seasons: number;
  totalGoals: number;
  totalAssists: number;
  totalTackles: number;
  totalCleanSheets: number;
  totalCaps: number;
}

export interface SaveResponse {
  score: number;
  breakdown: ScoreBreakdown;
  savedToSlot: number | null;
  titleCounts: Partial<Record<TitleKind, number>>;
  totals: CareerTotals;
}

export class ApiError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}
