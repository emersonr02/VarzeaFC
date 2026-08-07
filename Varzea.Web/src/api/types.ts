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
  | "RequestCaptaincy" | "RequestSetPieces" | "RequestLeaveNow" | "RequestLoan";

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

export interface SeasonResult {
  age: number;
  overall: number;
  clubTier: number;
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
  requestMade: SeasonRequestKind;
  requestGranted: boolean;
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
}

export interface PendingContractChoice {
  age: number;
  overall: number;
  proposals: ContractProposalOption[];
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
