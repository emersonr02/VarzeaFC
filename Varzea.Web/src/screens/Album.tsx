import { useEffect, useState } from "react";
import { POS_LABEL } from "../data/labels";
import type { AlbumRecord } from "./Result";

interface Props {
  onBack: () => void;
}

export function Album({ onBack }: Props) {
  const [records, setRecords] = useState<AlbumRecord[] | null>(null);

  useEffect(() => {
    try {
      const raw = localStorage.getItem("varzea_album");
      const list: AlbumRecord[] = raw ? JSON.parse(raw) : [];
      list.sort((a, b) => b.savedAt - a.savedAt);
      setRecords(list);
    } catch {
      setRecords([]);
    }
  }, []);

  return (
    <section className="screen pitch-bg">
      <div className="wrap">
        <div className="top-nav">
          <button className="back-btn" onClick={onBack}>← Menu</button>
          <span className="step-label">Coleção pessoal</span>
        </div>
        <h2 className="h2">Meu álbum de <span className="accent">figurinhas</span></h2>
        {records === null ? (
          <p className="empty-msg">Carregando álbum...</p>
        ) : records.length === 0 ? (
          <p className="empty-msg">Seu álbum está vazio. Jogue uma carreira e guarde a figurinha aqui! 🧷</p>
        ) : (
          <div className="album-grid">
            {records.map((r) => (
              <div className="album-slot" key={r.id}>
                <div className="a-ovr">{r.peak}</div>
                <div className="a-name">{r.nickname}</div>
                <div className="a-verdict">{r.verdictTitle}</div>
                <div style={{ fontSize: 10, color: "var(--ink-soft)", marginTop: 2 }}>{POS_LABEL[r.position]} · {r.country}</div>
                <div style={{ fontSize: 11, color: "var(--ink-soft)", marginTop: 4 }}>score {r.score.toFixed(1)}</div>
              </div>
            ))}
          </div>
        )}
        <p className="small-note">
          O álbum fica salvo só neste navegador (localStorage) — ainda não está ligado ao
          Postgres nem a uma conta de usuário (isso depende de um sistema de login, que
          o HANDOFF ainda não decidiu).
        </p>
      </div>
    </section>
  );
}
