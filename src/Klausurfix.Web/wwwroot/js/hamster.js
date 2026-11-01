let visibleHamster = null;
let hideTimer = null;

export function hideHamster() {
  clearTimeout(hideTimer);
  hideTimer = null;
  visibleHamster?.remove();
  visibleHamster = null;
}

export function showHamster() {
  hideHamster();
  const hamster = document.createElement("img");
  hamster.className = "deduction-hamster";
  hamster.src = "/assets/hamster.png";
  hamster.alt = "";
  hamster.setAttribute("aria-hidden", "true");
  hamster.addEventListener("error", () => { if (visibleHamster === hamster) hideHamster(); }, { once: true });
  document.body.appendChild(hamster);
  visibleHamster = hamster;
  hideTimer = setTimeout(hideHamster, 5000);
}
