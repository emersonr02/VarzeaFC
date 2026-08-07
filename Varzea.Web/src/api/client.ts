import type {
  AdvanceResponse,
  ApiError as ApiErrorType,
  DraftCompleteResponse,
  DraftRoundResponse,
  MetaResponse,
  Pos,
  PositionLockedResponse,
  SaveResponse,
  SeasonRequestKind,
} from "./types";
import { ApiError } from "./types";

async function send<T>(path: string, body?: unknown): Promise<T> {
  const res = await fetch(path, {
    method: body === undefined ? "GET" : "POST",
    headers: body === undefined ? undefined : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText);
    throw new ApiError(res.status, text || res.statusText);
  }
  return (await res.json()) as T;
}

export const api = {
  meta: () => send<MetaResponse>("/meta"),

  start: () => send<DraftRoundResponse>("/careers/start", {}),

  draft: (token: string, pick: number) =>
    send<DraftRoundResponse | DraftCompleteResponse>("/careers/draft", { token, pick }),

  position: (token: string, position: Pos, country: string) =>
    send<PositionLockedResponse>("/careers/position", { token, position, country }),

  // request só faz sentido quando decision e contractChoiceIndex são undefined
  // (avançando pra temporada nova, não resolvendo uma pausa pendente) — ver
  // Varzea.Api.Program /careers/advance. contractChoiceIndex resolve PendingContractChoice
  // (Roadmap §9 Bloco 3, "múltiplas propostas"); -1 = recusar todas as propostas.
  advance: (token: string, decision?: boolean, request?: SeasonRequestKind, contractChoiceIndex?: number) =>
    send<AdvanceResponse>("/careers/advance", {
      token,
      decision: decision ?? null,
      request: request ?? null,
      contractChoiceIndex: contractChoiceIndex ?? null,
    }),

  save: (token: string) => send<SaveResponse>("/careers/save", { token }),
};

export type { ApiErrorType };

/// Distingue a resposta de draft "rodada seguinte" da "draft completo" (o 2º não tem
/// `candidates`) — a API devolve formas diferentes no mesmo par de chamadas.
export function isDraftComplete(
  r: DraftRoundResponse | DraftCompleteResponse
): r is DraftCompleteResponse {
  return "attributes" in r;
}
