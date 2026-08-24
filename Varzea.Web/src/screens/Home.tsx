import { HEADLINES } from "../data/news";
import { VERDICTS } from "../data/verdicts";

interface Props {
  onPlay: () => void;
  onAlbum: () => void;
}

// Ticker de manchetes. O conteúdo é duplicado de propósito: a animação desliza até
// -50% e reinicia, e como a segunda metade é idêntica à primeira o corte fica
// invisível — é o truque padrão de marquee sem biblioteca.
function NewsTicker() {
  const strip = (
    <div className="ticker-strip">
      {HEADLINES.map((h, i) => (
        <span className="ticker-item" key={i}>
          <span className="ticker-dot" />
          <b>{h.tag}:</b> {h.text}
        </span>
      ))}
    </div>
  );
  return (
    <div className="news-ticker" aria-label="Manchetes do Várzea Lendas">
      {strip}
      {/* cópia só pra emendar o loop — escondida de leitores de tela */}
      <div aria-hidden="true" style={{ display: "contents" }}>{strip}</div>
    </div>
  );
}

// Cards de "até onde dá pra chegar". Usam os VEREDICTOS REAIS do jogo (data/verdicts.ts),
// com o score mínimo de cada um — não são números decorativos: é exatamente o corte que
// o placar aplica no fim da carreira.
const SHOWCASE = [
  { tier: 1, note: "onde a maioria termina", accent: "rgba(255,255,255,0.45)" },
  { tier: 7, note: "uma carreira especial", accent: "var(--gold)" },
  { tier: 11, note: "o sonho", accent: "var(--rust)" },
];

function TierShowcase() {
  return (
    <div className="tier-grid">
      {SHOWCASE.map(({ tier, note, accent }) => {
        const v = VERDICTS.find((x) => x.tier === tier)!;
        return (
          <div className="tier-card" key={tier} style={{ borderColor: accent }}>
            <div className="tier-score" style={{ color: accent }}>
              {v.min === -Infinity ? "0" : v.min}
              <span className="tier-score-lbl">pontos</span>
            </div>
            <div className="tier-name">{v.title}</div>
            <div className="tier-note">{note}</div>
            <div className="tier-desc">{v.desc}</div>
            {v.needsBallonOr && <div className="tier-req">exige Bola de Ouro</div>}
          </div>
        );
      })}
    </div>
  );
}

export function Home({ onPlay, onAlbum }: Props) {
  return (
    <section className="screen pitch-bg">
      <NewsTicker />

      <div className="wrap">
        <span className="badge-eyebrow">⚽ Fantasia de futebol · grátis · sem cadastro</span>
        <h1 className="title-hero">
          VÁRZEA<br /><span className="accent">LENDAS</span>
        </h1>
        <p className="subtitle">
          Roube atributos de lendas, descubra seu potencial em cada posição e viva a carreira
          inteira: liga, copas, seleção, transferências e lesões.
        </p>

        <button className="btn rust btn-hero" onClick={onPlay}>Jogar agora ⚽</button>

        <div className="section-label">Até onde dá pra chegar</div>
        <TierShowcase />

        <div className="section-label">Escolha o modo</div>
        <div className="mode-grid">
          <div className="mode-card" onClick={onPlay}>
            <span className="mode-tag">Modo Jogador</span>
            <div className="mode-title">Draft &amp; Carreira</div>
            <div className="mode-desc">Nome, nacionalidade, draft de 8 atributos, escolha sua posição e viva a carreira inteira.</div>
          </div>
          <div className="mode-card disabled" title="Em breve">
            <span className="soon-pill">Em breve</span>
            <span className="mode-tag">Modo Técnico</span>
            <div className="mode-title">Prancheta &amp; Banco</div>
            <div className="mode-desc">Crie seu treinador, escolha um estilo tático e construa seu legado na beira do campo.</div>
          </div>
        </div>

        <div className="foot-links">
          <a onClick={onAlbum}>📖 Meu álbum de figurinhas</a>
        </div>

        <p className="small-note">
          O motor de simulação roda no servidor — o resultado da sua carreira é sempre
          recalculado lá, nunca inventado no navegador.
        </p>
      </div>
    </section>
  );
}
