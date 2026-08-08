import { useEffect, useState } from "react";

// Logo real dos clubes (roadmap pós-§9, "quero logo real") — busca na TheSportsDB
// (API pública gratuita p/ uso não-comercial, key de teste "3" documentada por eles
// pra esse fim) em vez de baixar/hospedar imagem de crest direto: menor risco de
// direitos autorais, já que é o canal que o próprio provedor disponibiliza. A base de
// clubs.json tem ~400 clubes (muitos times pequenos de 2ª divisão); nem todo mundo tem
// entrada lá — quem não tem cai no ClubBadge de iniciais como reserva, sem erro visível.
const cache = new Map<string, string | null>();
const inflight = new Map<string, Promise<string | null>>();

async function fetchClubLogo(clubName: string): Promise<string | null> {
  try {
    const res = await fetch(`https://www.thesportsdb.com/api/v1/json/3/searchteams.php?t=${encodeURIComponent(clubName)}`);
    if (!res.ok) return null;
    const data = await res.json();
    const badge = data?.teams?.[0]?.strBadge;
    return typeof badge === "string" && badge.length > 0 ? badge : null;
  } catch {
    return null;
  }
}

export function useClubLogo(clubName: string): string | null {
  const [url, setUrl] = useState<string | null>(cache.get(clubName) ?? null);

  useEffect(() => {
    if (cache.has(clubName)) {
      setUrl(cache.get(clubName) ?? null);
      return;
    }
    let cancelled = false;
    let promise = inflight.get(clubName);
    if (!promise) {
      promise = fetchClubLogo(clubName);
      inflight.set(clubName, promise);
    }
    promise.then((result) => {
      cache.set(clubName, result);
      inflight.delete(clubName);
      if (!cancelled) setUrl(result);
    });
    return () => {
      cancelled = true;
    };
  }, [clubName]);

  return url;
}
