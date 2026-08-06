import { useEffect, useState } from "react";
import { api } from "../api/client";
import type { CareerTotals, Pos, ScoreBreakdown, TitleKind } from "../api/types";
import { StickerCard } from "../components/StickerCard";
import { computeVerdict } from "../data/verdicts";

interface Props {
  token: string;
  nickname: string;
  country: string;
  position: Pos;
  potential: number;
  role: string;
  attrs: number[];
  onSaveComplete: (record: AlbumRecord) => void;
  onNewCareer: () => void;
  onHome: () => void;
}

export interface AlbumRecord {
  id: string;
  nickname: string;
  position: Pos;
  country: string;
  peak: number;
  score: number;
  verdictTitle: string;
  savedAt: number;
}

interface SaveState {
  score: number;
  breakdown: ScoreBreakdown;
  titleCounts: Partial<Record<TitleKind, number>>;
  totals: CareerTotals;
}

export function Result({ token, nickname, country, position, potential, role, attrs, onSaveComplete, onNewCareer, onHome }: Props) {
  const [data, setData] = useState<SaveState | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [savedLocally, setSavedLocally] = useState(false);

  useEffect(() => {
    let cancelled = false;
    api
      .save(token)
      .then((resp) => { if (!cancelled) setData(resp); })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : "Erro ao calcular o resultado."); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [token]);

  if (loading) {
    return (
      <section className="screen pitch-bg">
        <div className="wrap"><p className="empty-msg">Calculando o veredito da torcida…</p></div>
      </section>
    );
  }
  if (error || !data) {
    return (
      <section className="screen pitch-bg">
        <div className="wrap">
          <div className="error-banner">{error ?? "Não foi possível calcular o resultado."}</div>
          <button className="btn secondary" onClick={onHome}>Voltar ao menu</button>
        </div>
      </section>
    );
  }

  const verdict = computeVerdict(data.score, data.titleCounts);
  const INDIVIDUAL_AWARDS: TitleKind[] = ["BallonDOr", "TeamOfTheYear", "KingOfAmerica", "SouthAmericanTeamOfTheYear"];
  const totalTitles = Object.entries(data.titleCounts)
    .filter(([k]) => !INDIVIDUAL_AWARDS.includes(k as TitleKind))
    .reduce((sum, [, n]) => sum + (n ?? 0), 0);

  function saveToAlbum() {
    if (!data) return;
    try {
      const record: AlbumRecord = {
        id: `career_${Date.now()}`,
        nickname, position, country,
        peak: data.totals.peakOverall,
        score: data.score,
        verdictTitle: verdict.title,
        savedAt: Date.now(),
      };
      const raw = localStorage.getItem("varzea_album");
      const list: AlbumRecord[] = raw ? JSON.parse(raw) : [];
      list.push(record);
      localStorage.setItem("varzea_album", JSON.stringify(list));
      setSavedLocally(true);
      onSaveComplete(record);
    } catch {
      setError("Não foi possível salvar no álbum agora.");
    }
  }

  const shareText = `⚽ VÁRZEA LENDAS\n${nickname} — score ${data.score.toFixed(1)}\nVeredito: ${verdict.title} (nível ${verdict.tier}/11)\nPico: ${data.totals.peakOverall} OVR · ${data.totals.totalGoals} gols · ${data.totals.totalAssists} assist. · ${totalTitles} títulos · ${data.titleCounts.BallonDOr ?? 0} Bola(s) de Ouro`;

  return (
    <section className="screen pitch-bg">
      <div className="wrap">
        <div className="top-nav">
          <button className="back-btn" onClick={onHome}>← Menu</button>
          <span className="step-label">Fim de carreira</span>
        </div>
        <div className="verdict-box">
          <div className="verdict-eyebrow">O veredito da torcida</div>
          <div className="verdict-title">{verdict.title}</div>
          <p className="verdict-desc">{verdict.desc}</p>
          <div className="verdict-tier">Nível {verdict.tier} de 11 · score {data.score.toFixed(1)}</div>
        </div>
        <div className="stat-grid">
          <div className="stat-box"><div className="stat-num">{data.totals.peakOverall}</div><div className="stat-lbl">Overall de pico</div></div>
          <div className="stat-box"><div className="stat-num">{data.totals.seasons}</div><div className="stat-lbl">Temporadas</div></div>
          <div className="stat-box"><div className="stat-num">{data.totals.totalGoals}</div><div className="stat-lbl">Gols na carreira</div></div>
          <div className="stat-box"><div className="stat-num">{data.totals.totalAssists}</div><div className="stat-lbl">Assistências</div></div>
          <div className="stat-box"><div className="stat-num">{totalTitles}</div><div className="stat-lbl">Títulos totais</div></div>
          <div className="stat-box"><div className="stat-num">{data.totals.totalCaps}</div><div className="stat-lbl">Jogos pela seleção</div></div>
        </div>
        <div className="sticker-stage">
          <StickerCard nickname={nickname} country={country} position={position} potential={potential} role={role} attrs={attrs} />
        </div>
        <div style={{ display: "flex", gap: 10, flexWrap: "wrap", justifyContent: "center", marginTop: 6 }}>
          <button className="btn rust" disabled={savedLocally} onClick={saveToAlbum}>
            {savedLocally ? "✔ Guardado" : "📌 Guardar no álbum"}
          </button>
          <button className="btn secondary" onClick={onNewCareer}>🔁 Nova carreira</button>
        </div>
        <div className="share-box">
          <div className="dock-title">Compartilhar resultado</div>
          <textarea readOnly value={shareText} />
        </div>
      </div>
    </section>
  );
}
