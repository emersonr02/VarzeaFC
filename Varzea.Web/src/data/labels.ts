import type { Attr, InjurySeverity, Pos, SeasonRequestKind, TitleKind } from "../api/types";

// Mapeia os enums do motor (Varzea.Engine.Model) pros rótulos em português do
// varzea-lendas.html original (POC aprovado). Os CÓDIGOS mudaram (GK/CB/FB... em vez de
// GOL/ZAG/LAT...) porque vêm do motor real, mas os NOMES em português são os mesmos.
export const POS_LABEL: Record<Pos, string> = {
  GK: "Goleiro",
  CB: "Zagueiro",
  FB: "Lateral",
  DM: "Volante",
  CM: "Meia",
  AM: "Meia-Atacante",
  W: "Ponta",
  SS: "Segundo Atacante",
  ST: "Centroavante",
};

export const POS_ORDER: Pos[] = ["GK", "CB", "FB", "DM", "CM", "AM", "W", "SS", "ST"];

export const ATTR_LABEL: Record<Attr, string> = {
  Pac: "RITMO",
  Sho: "FINALIZAÇÃO",
  Pas: "PASSE",
  Dri: "DRIBLE",
  Def: "DEFESA",
  Phy: "FÍSICO",
  Ski: "HABILIDADE",
  Wea: "PÉ FRACO",
};

export const ATTR_SHORT: Record<Attr, string> = {
  Pac: "RIT",
  Sho: "FIN",
  Pas: "PAS",
  Dri: "DRI",
  Def: "DEF",
  Phy: "FÍS",
  Ski: "HAB",
  Wea: "PFR",
};

export const TITLE_LABEL: Record<TitleKind, string> = {
  LeagueTop5: "Campeão da Liga",
  LeagueMid: "Campeão da Liga",
  LeagueMinor: "Campeão da Liga",
  DomesticCup: "Copa Nacional",
  ContinentalSecondary: "Copa Continental",
  ContinentalPrimary: "Liga dos Campeões",
  WorldCup: "Copa do Mundo",
  BallonDOr: "Bola de Ouro",
  TeamOfTheYear: "Equipe do Ano",
  SouthAmericanTeamOfTheYear: "Equipe do Ano da América",
  KingOfAmerica: "Rei da América",
};

// Painel Contrato + Técnico (roadmap pós-§9) — rótulo curto de botão.
export const SEASON_REQUEST_BUTTON_LABEL: Record<Exclude<SeasonRequestKind, "None">, string> = {
  RequestRenewal: "Pedir renovação",
  RequestLeaveAtContractEnd: "Avisar que quer sair no fim do contrato",
  RequestRaise: "Pedir aumento",
  RequestCaptaincy: "Pedir a braçadeira",
  RequestSetPieces: "Pedir bolas paradas",
  RequestLeaveNow: "Pedir pra sair JÁ",
};

export const INJURY_LABEL: Record<InjurySeverity, string | null> = {
  None: null,
  Minor: "Lesão leve tirou você de parte da temporada.",
  Moderate: "Lesão moderada tirou você de parte da temporada.",
  Severe: "Lesão grave tirou você de boa parte da temporada.",
  CareerEnding: "Uma lesão grave encerra a carreira antes da hora.",
};

export const ATTR_ORDER: Attr[] = ["Pac", "Sho", "Pas", "Dri", "Def", "Phy", "Ski", "Wea"];

export const ATTR_CATEGORY: Record<Attr, string> = {
  Sho: "TÉCNICA", Pas: "TÉCNICA", Dri: "TÉCNICA", Ski: "TÉCNICA", Wea: "TÉCNICA",
  Pac: "FÍSICA", Phy: "FÍSICA",
  Def: "DEFESA/MENTAL",
};
export const CATEGORY_ORDER = ["TÉCNICA", "FÍSICA", "DEFESA/MENTAL"];

export function initials(name: string): string {
  return name
    .split(" ")
    .map((w) => w[0])
    .slice(0, 2)
    .join("")
    .toUpperCase();
}
