// Espelha Varzea.Api/Contracts.cs e Varzea.Engine/Model/Domain.cs. Mantido manualmente —
// se a API mudar um contrato, este arquivo precisa acompanhar (não há geração automática
// ainda; um gerador de cliente a partir do OpenAPI seria o próximo passo natural).

export type Pos = "GK" | "CB" | "FB" | "DM" | "CM" | "AM" | "W" | "SS" | "ST";
export type Attr = "Pac" | "Sho" | "Pas" | "Dri" | "Def" | "Phy" | "Ski" | "Wea";
export type InjurySeverity = "None" | "Minor" | "Moderate" | "Severe" | "CareerEnding";
export type TitleKind =
  | "LeagueTop5" | "LeagueMid" | "LeagueMinor" | "DomesticCup"
  | "ContinentalSecondary" | "ContinentalPrimary" | "WorldCup"
  | "BallonDOr" | "TeamOfTheYear";

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
}

export interface AdvanceResponse {
  token: string;
  newSeasons: SeasonResult[];
  pendingOffer: PendingTransferOffer | null;
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
