const state = {
  token: localStorage.getItem("phoneControlToken") || "",
  language: "en",
  devices: [],
  fanGroups: [],
  themes: [],
  effects: [],
  selectedDeviceId: "",
  selectedFanGroupId: "",
  selectedEffectId: "meteor",
  fanMerge: false,
  selectedDirection: 0,
  selectedSpeed: 50,
  brightnessTimer: 0,
  ledBrightnessTimer: 0
};

const i18n = {
  en: {
    appTitle: "Phone Link", connecting: "Connecting...", live: "Live", settings: "Settings", reconnect: "Reconnect", close: "Close",
    pin: "PIN", connect: "Connect", activeDevice: "Active device", fanGroup: "Fan group", screen: "Screen",
    screenDesc: "LCD backlight brightness", lighting: "Lighting", lightingDesc: "LED effect and effect brightness",
    groupMerge: "Group merge", effect: "Effect", colors: "Colors", mainColors: "Main colors", speed: "Speed",
    direction: "Direction", applySelectedGroup: "Apply selected group", applyLighting: "Apply lighting", applyAllFans: "Apply to all TL W",
    library: "Library", themes: "Themes", forgetPin: "Forget PIN", usePin: "Use PIN", savePin: "Save PIN", port: "Port",
    checking: "Checking L-Connect...", onlinePort: "L-Connect online on port {0}", online: "Online", offline: "Offline",
    required: "Required", off: "Off", enterPin: "Enter a PIN first.", pinEnabled: "PIN enabled.", pinDisabled: "PIN disabled.",
    pinSaved: "PIN saved.", noDevice: "No LCD device found.", fanReady: "Fan group control ready.", noFanGroup: "No wireless fan group found.",
    loadingThemes: "Loading themes...", ready: "Ready", noThemes: "No themes found.", selected: "Selected", apply: "Apply",
    applying: "Applying", themeApplied: "Theme applied.", forward: "Forward", reverse: "Reverse",
    brightnessUpdated: "Brightness updated.", ledBrightnessSet: "LED brightness set to {0}.", lightingUpdated: "Lighting updated.",
    allFansUpdated: "All TL W groups updated.", colorCountMany: "{0} main colors", colorCountOne: "1 main color",
    pinCleared: "PIN cleared.", pinRequired: "PIN required.", requestFailed: "Request failed."
  },
  tr: {
    appTitle: "Phone Link", connecting: "Bağlanıyor...", live: "Canlı", settings: "Ayarlar", reconnect: "Yeniden bağlan", close: "Kapat",
    pin: "PIN", connect: "Bağlan", activeDevice: "Aktif cihaz", fanGroup: "Fan grubu", screen: "Ekran",
    screenDesc: "LCD arka ışık parlaklığı", lighting: "Aydınlatma", lightingDesc: "LED efekti ve efekt parlaklığı",
    groupMerge: "Grup birleştirme", effect: "Efekt", colors: "Renkler", mainColors: "Ana renkler", speed: "Hız",
    direction: "Yön", applySelectedGroup: "Seçili gruba uygula", applyLighting: "Aydınlatmayı uygula", applyAllFans: "Tüm TL W'ye uygula",
    library: "Kütüphane", themes: "Temalar", forgetPin: "PIN'i unut", usePin: "PIN kullan", savePin: "PIN'i kaydet", port: "Port",
    checking: "L-Connect kontrol ediliyor...", onlinePort: "L-Connect {0} portunda çevrimiçi", online: "Çevrimiçi", offline: "Çevrimdışı",
    required: "Gerekli", off: "Kapalı", enterPin: "Önce PIN girin.", pinEnabled: "PIN etkinleştirildi.", pinDisabled: "PIN devre dışı.",
    pinSaved: "PIN kaydedildi.", noDevice: "LCD cihaz bulunamadı.", fanReady: "Fan grubu kontrolü hazır.", noFanGroup: "Kablosuz fan grubu bulunamadı.",
    loadingThemes: "Temalar yükleniyor...", ready: "Hazır", noThemes: "Tema bulunamadı.", selected: "Seçili", apply: "Uygula",
    applying: "Uygulanıyor", themeApplied: "Tema uygulandı.", forward: "İleri", reverse: "Ters",
    brightnessUpdated: "Parlaklık güncellendi.", ledBrightnessSet: "LED parlaklığı {0} olarak ayarlandı.", lightingUpdated: "Aydınlatma güncellendi.",
    allFansUpdated: "Tüm TL W grupları güncellendi.", colorCountMany: "{0} ana renk", colorCountOne: "1 ana renk",
    pinCleared: "PIN temizlendi.", pinRequired: "PIN gerekli.", requestFailed: "İstek başarısız."
  },
  de: {
    appTitle: "Phone Link", connecting: "Verbinden...", live: "Live", settings: "Einstellungen", reconnect: "Neu verbinden", close: "Schließen",
    pin: "PIN", connect: "Verbinden", activeDevice: "Aktives Gerät", fanGroup: "Lüftergruppe", screen: "Bildschirm",
    screenDesc: "LCD-Hintergrundhelligkeit", lighting: "Beleuchtung", lightingDesc: "LED-Effekt und Effekt-Helligkeit",
    groupMerge: "Gruppe zusammenführen", effect: "Effekt", colors: "Farben", mainColors: "Hauptfarben", speed: "Geschwindigkeit",
    direction: "Richtung", applySelectedGroup: "Auf ausgewählte Gruppe anwenden", applyLighting: "Beleuchtung anwenden", applyAllFans: "Auf alle TL W anwenden",
    library: "Bibliothek", themes: "Themes", forgetPin: "PIN vergessen", usePin: "PIN verwenden", savePin: "PIN speichern", port: "Port",
    checking: "L-Connect wird geprüft...", onlinePort: "L-Connect online auf Port {0}", online: "Online", offline: "Offline",
    required: "Erforderlich", off: "Aus", enterPin: "Geben Sie zuerst eine PIN ein.", pinEnabled: "PIN aktiviert.", pinDisabled: "PIN deaktiviert.",
    pinSaved: "PIN gespeichert.", noDevice: "Kein LCD-Gerät gefunden.", fanReady: "Lüftergruppensteuerung bereit.", noFanGroup: "Keine drahtlose Lüftergruppe gefunden.",
    loadingThemes: "Themes werden geladen...", ready: "Bereit", noThemes: "Keine Themes gefunden.", selected: "Ausgewählt", apply: "Anwenden",
    applying: "Wird angewendet", themeApplied: "Theme angewendet.", forward: "Vorwärts", reverse: "Rückwärts",
    brightnessUpdated: "Helligkeit aktualisiert.", ledBrightnessSet: "LED-Helligkeit auf {0} gesetzt.", lightingUpdated: "Beleuchtung aktualisiert.",
    allFansUpdated: "Alle TL W-Gruppen aktualisiert.", colorCountMany: "{0} Hauptfarben", colorCountOne: "1 Hauptfarbe",
    pinCleared: "PIN gelöscht.", pinRequired: "PIN erforderlich.", requestFailed: "Anfrage fehlgeschlagen."
  },
  fr: {
    appTitle: "Phone Link", connecting: "Connexion...", live: "Direct", settings: "Paramètres", reconnect: "Reconnecter", close: "Fermer",
    pin: "PIN", connect: "Connecter", activeDevice: "Appareil actif", fanGroup: "Groupe de ventilateurs", screen: "Écran",
    screenDesc: "Luminosité du rétroéclairage LCD", lighting: "Éclairage", lightingDesc: "Effet LED et luminosité de l'effet",
    groupMerge: "Fusion du groupe", effect: "Effet", colors: "Couleurs", mainColors: "Couleurs principales", speed: "Vitesse",
    direction: "Direction", applySelectedGroup: "Appliquer au groupe sélectionné", applyLighting: "Appliquer l'éclairage", applyAllFans: "Appliquer à tous les TL W",
    library: "Bibliothèque", themes: "Thèmes", forgetPin: "Oublier le PIN", usePin: "Utiliser un PIN", savePin: "Enregistrer le PIN", port: "Port",
    checking: "Vérification de L-Connect...", onlinePort: "L-Connect en ligne sur le port {0}", online: "En ligne", offline: "Hors ligne",
    required: "Requis", off: "Désactivé", enterPin: "Saisissez d'abord un PIN.", pinEnabled: "PIN activé.", pinDisabled: "PIN désactivé.",
    pinSaved: "PIN enregistré.", noDevice: "Aucun appareil LCD trouvé.", fanReady: "Contrôle du groupe prêt.", noFanGroup: "Aucun groupe de ventilateurs sans fil trouvé.",
    loadingThemes: "Chargement des thèmes...", ready: "Prêt", noThemes: "Aucun thème trouvé.", selected: "Sélectionné", apply: "Appliquer",
    applying: "Application", themeApplied: "Thème appliqué.", forward: "Avant", reverse: "Inverse",
    brightnessUpdated: "Luminosité mise à jour.", ledBrightnessSet: "Luminosité LED définie sur {0}.", lightingUpdated: "Éclairage mis à jour.",
    allFansUpdated: "Tous les groupes TL W ont été mis à jour.", colorCountMany: "{0} couleurs principales", colorCountOne: "1 couleur principale",
    pinCleared: "PIN effacé.", pinRequired: "PIN requis.", requestFailed: "Échec de la requête."
  },
  ru: {
    appTitle: "Phone Link", connecting: "Подключение...", live: "Активно", settings: "Настройки", reconnect: "Переподключить", close: "Закрыть",
    pin: "PIN", connect: "Подключить", activeDevice: "Активное устройство", fanGroup: "Группа вентиляторов", screen: "Экран",
    screenDesc: "Яркость подсветки LCD", lighting: "Подсветка", lightingDesc: "LED-эффект и яркость эффекта",
    groupMerge: "Объединение группы", effect: "Эффект", colors: "Цвета", mainColors: "Основные цвета", speed: "Скорость",
    direction: "Направление", applySelectedGroup: "Применить к выбранной группе", applyLighting: "Применить подсветку", applyAllFans: "Применить ко всем TL W",
    library: "Библиотека", themes: "Темы", forgetPin: "Забыть PIN", usePin: "Использовать PIN", savePin: "Сохранить PIN", port: "Порт",
    checking: "Проверка L-Connect...", onlinePort: "L-Connect онлайн на порту {0}", online: "Онлайн", offline: "Офлайн",
    required: "Требуется", off: "Выкл.", enterPin: "Сначала введите PIN.", pinEnabled: "PIN включен.", pinDisabled: "PIN отключен.",
    pinSaved: "PIN сохранен.", noDevice: "LCD-устройство не найдено.", fanReady: "Управление группой готово.", noFanGroup: "Беспроводная группа вентиляторов не найдена.",
    loadingThemes: "Загрузка тем...", ready: "Готово", noThemes: "Темы не найдены.", selected: "Выбрано", apply: "Применить",
    applying: "Применение", themeApplied: "Тема применена.", forward: "Вперед", reverse: "Назад",
    brightnessUpdated: "Яркость обновлена.", ledBrightnessSet: "Яркость LED установлена на {0}.", lightingUpdated: "Подсветка обновлена.",
    allFansUpdated: "Все группы TL W обновлены.", colorCountMany: "{0} основных цветов", colorCountOne: "1 основной цвет",
    pinCleared: "PIN очищен.", pinRequired: "Требуется PIN.", requestFailed: "Запрос не выполнен."
  },
  zh: {
    appTitle: "Phone Link", connecting: "正在连接...", live: "实时", settings: "设置", reconnect: "重新连接", close: "关闭",
    pin: "PIN", connect: "连接", activeDevice: "活动设备", fanGroup: "风扇组", screen: "屏幕",
    screenDesc: "LCD 背光亮度", lighting: "灯光", lightingDesc: "LED 效果和效果亮度",
    groupMerge: "组合并", effect: "效果", colors: "颜色", mainColors: "主颜色", speed: "速度",
    direction: "方向", applySelectedGroup: "应用到所选组", applyLighting: "应用灯光", applyAllFans: "应用到所有 TL W",
    library: "库", themes: "主题", forgetPin: "忘记 PIN", usePin: "使用 PIN", savePin: "保存 PIN", port: "端口",
    checking: "正在检查 L-Connect...", onlinePort: "L-Connect 在端口 {0} 在线", online: "在线", offline: "离线",
    required: "必需", off: "关闭", enterPin: "请先输入 PIN。", pinEnabled: "PIN 已启用。", pinDisabled: "PIN 已禁用。",
    pinSaved: "PIN 已保存。", noDevice: "未找到 LCD 设备。", fanReady: "风扇组控制已就绪。", noFanGroup: "未找到无线风扇组。",
    loadingThemes: "正在加载主题...", ready: "就绪", noThemes: "未找到主题。", selected: "已选择", apply: "应用",
    applying: "正在应用", themeApplied: "主题已应用。", forward: "正向", reverse: "反向",
    brightnessUpdated: "亮度已更新。", ledBrightnessSet: "LED 亮度已设为 {0}。", lightingUpdated: "灯光已更新。",
    allFansUpdated: "所有 TL W 组已更新。", colorCountMany: "{0} 个主颜色", colorCountOne: "1 个主颜色",
    pinCleared: "PIN 已清除。", pinRequired: "需要 PIN。", requestFailed: "请求失败。"
  },
  ko: {
    appTitle: "Phone Link", connecting: "연결 중...", live: "라이브", settings: "설정", reconnect: "다시 연결", close: "닫기",
    pin: "PIN", connect: "연결", activeDevice: "활성 장치", fanGroup: "팬 그룹", screen: "화면",
    screenDesc: "LCD 백라이트 밝기", lighting: "조명", lightingDesc: "LED 효과 및 효과 밝기",
    groupMerge: "그룹 병합", effect: "효과", colors: "색상", mainColors: "주요 색상", speed: "속도",
    direction: "방향", applySelectedGroup: "선택한 그룹에 적용", applyLighting: "조명 적용", applyAllFans: "모든 TL W에 적용",
    library: "라이브러리", themes: "테마", forgetPin: "PIN 지우기", usePin: "PIN 사용", savePin: "PIN 저장", port: "포트",
    checking: "L-Connect 확인 중...", onlinePort: "L-Connect가 포트 {0}에서 온라인", online: "온라인", offline: "오프라인",
    required: "필수", off: "꺼짐", enterPin: "먼저 PIN을 입력하세요.", pinEnabled: "PIN이 활성화되었습니다.", pinDisabled: "PIN이 비활성화되었습니다.",
    pinSaved: "PIN이 저장되었습니다.", noDevice: "LCD 장치를 찾을 수 없습니다.", fanReady: "팬 그룹 제어 준비됨.", noFanGroup: "무선 팬 그룹을 찾을 수 없습니다.",
    loadingThemes: "테마 로드 중...", ready: "준비됨", noThemes: "테마를 찾을 수 없습니다.", selected: "선택됨", apply: "적용",
    applying: "적용 중", themeApplied: "테마가 적용되었습니다.", forward: "정방향", reverse: "역방향",
    brightnessUpdated: "밝기가 업데이트되었습니다.", ledBrightnessSet: "LED 밝기가 {0}(으)로 설정되었습니다.", lightingUpdated: "조명이 업데이트되었습니다.",
    allFansUpdated: "모든 TL W 그룹이 업데이트되었습니다.", colorCountMany: "주요 색상 {0}개", colorCountOne: "주요 색상 1개",
    pinCleared: "PIN이 삭제되었습니다.", pinRequired: "PIN이 필요합니다.", requestFailed: "요청 실패."
  }
};

function t(key, ...values) {
  const table = i18n[state.language] || i18n.en;
  let value = table[key] || i18n.en[key] || key;
  values.forEach((item, index) => {
    value = value.replace(`{${index}}`, item);
  });
  return value;
}

function applyLanguage(language) {
  state.language = i18n[language] ? language : "en";
  document.documentElement.lang = state.language;
  for (const element of document.querySelectorAll("[data-i18n]")) {
    element.textContent = t(element.dataset.i18n);
  }
  for (const element of document.querySelectorAll("[data-i18n-title]")) {
    element.title = t(element.dataset.i18nTitle);
  }
  for (const element of document.querySelectorAll("[data-i18n-aria]")) {
    element.setAttribute("aria-label", t(element.dataset.i18nAria));
  }
  for (const element of document.querySelectorAll("[data-i18n-placeholder]")) {
    element.placeholder = t(element.dataset.i18nPlaceholder);
  }
  renderDirectionSegments();
}

const els = {
  statusText: document.getElementById("statusText"),
  authPanel: document.getElementById("authPanel"),
  tokenInput: document.getElementById("tokenInput"),
  saveTokenButton: document.getElementById("saveTokenButton"),
  settingsButton: document.getElementById("settingsButton"),
  closeSettingsButton: document.getElementById("closeSettingsButton"),
  settingsModal: document.getElementById("settingsModal"),
  forgetTokenButton: document.getElementById("forgetTokenButton"),
  usePinCheck: document.getElementById("usePinCheck"),
  settingsPinInput: document.getElementById("settingsPinInput"),
  savePinButton: document.getElementById("savePinButton"),
  refreshButton: document.getElementById("refreshButton"),
  deviceSelect: document.getElementById("deviceSelect"),
  fanGroupPanel: document.getElementById("fanGroupPanel"),
  fanGroupSelect: document.getElementById("fanGroupSelect"),
  screenCard: document.getElementById("screenCard"),
  brightnessShell: document.getElementById("brightnessShell"),
  brightnessSlider: document.getElementById("brightnessSlider"),
  brightnessValue: document.getElementById("brightnessValue"),
  brightnessSegments: document.getElementById("brightnessSegments"),
  ledBrightnessShell: document.getElementById("ledBrightnessShell"),
  ledBrightnessSlider: document.getElementById("ledBrightnessSlider"),
  ledBrightnessValue: document.getElementById("ledBrightnessValue"),
  effectSelect: document.getElementById("effectSelect"),
  fanMergeRow: document.getElementById("fanMergeRow"),
  fanMergeCheck: document.getElementById("fanMergeCheck"),
  colorInputs: document.getElementById("colorInputs"),
  colorPalette: document.getElementById("colorPalette"),
  colorSlotHint: document.getElementById("colorSlotHint"),
  applyEffectButton: document.getElementById("applyEffectButton"),
  applyAllFansButton: document.getElementById("applyAllFansButton"),
  ledBrightnessSegments: document.getElementById("ledBrightnessSegments"),
  speedSegments: document.getElementById("speedSegments"),
  speedValue: document.getElementById("speedValue"),
  directionSegments: document.getElementById("directionSegments"),
  directionValue: document.getElementById("directionValue"),
  themesSection: document.getElementById("themesSection"),
  themeGrid: document.getElementById("themeGrid"),
  themeCount: document.getElementById("themeCount"),
  portValue: document.getElementById("portValue"),
  pinModeValue: document.getElementById("pinModeValue"),
  lConnectValue: document.getElementById("lConnectValue"),
  urlList: document.getElementById("urlList"),
  toast: document.getElementById("toast")
};

init();

function init() {
  preventDoubleTapZoom();
  els.tokenInput.value = state.token;
  els.saveTokenButton.addEventListener("click", saveToken);
  els.settingsButton.addEventListener("click", openSettings);
  els.closeSettingsButton.addEventListener("click", closeSettings);
  els.settingsModal.addEventListener("click", event => {
    if (event.target === els.settingsModal) {
      closeSettings();
    }
  });
  els.forgetTokenButton.addEventListener("click", forgetToken);
  els.usePinCheck.addEventListener("change", setUsePin);
  els.savePinButton.addEventListener("click", savePinSetting);
  els.refreshButton.addEventListener("click", refreshAll);
  els.deviceSelect.addEventListener("change", () => {
    state.selectedDeviceId = els.deviceSelect.value;
    handleTargetChanged();
  });
  els.fanGroupSelect.addEventListener("change", () => {
    state.selectedFanGroupId = els.fanGroupSelect.value;
    loadEffects();
  });
  els.fanMergeCheck.addEventListener("change", () => {
    state.fanMerge = els.fanMergeCheck.checked;
    loadEffects();
  });
  els.effectSelect.addEventListener("change", () => {
    state.selectedEffectId = els.effectSelect.value;
    syncEffectColor();
  });
  els.colorInputs.addEventListener("input", event => {
    if (event.target.matches("input[type='color']")) {
      updateAccentFromColors();
    }
  });
  els.colorInputs.addEventListener("click", event => {
    const chip = event.target.closest(".colorChip");
    if (chip) {
      setActiveColorSlot(Number(chip.dataset.index || 0));
    }
  });
  els.colorPalette.addEventListener("click", event => {
    const button = event.target.closest("button[data-color]");
    if (button) {
      setActiveSlotColor(button.dataset.color);
    }
  });
  els.brightnessSlider.addEventListener("input", onBrightnessInput);
  els.brightnessShell.addEventListener("pointerdown", onBrightnessPointer);
  els.ledBrightnessSlider.addEventListener("input", onLedBrightnessInput);
  els.applyEffectButton.addEventListener("click", applyLightingEffect);
  els.applyAllFansButton.addEventListener("click", applyLightingEffectToAllFans);
  renderBrightnessSegments(els.brightnessSegments, Number(els.brightnessSlider.value), setBrightnessFromSegment, true);
  renderBrightnessSegments(els.ledBrightnessSegments, Number(els.ledBrightnessSlider.value), setLedBrightnessFromSegment);
  renderSpeedSegments();
  renderDirectionSegments();
  renderHexPalette();
  updateBrightnessUi(Number(els.brightnessSlider.value));
  updateLedBrightnessUi(Number(els.ledBrightnessSlider.value));
  refreshAll();
}

async function refreshAll() {
  setStatus(t("checking"));
  els.refreshButton.classList.add("spinning");
  els.refreshButton.disabled = true;
  try {
    const config = await api("/api/config", { auth: false });
    renderConfig(config);
    els.authPanel.classList.toggle("hidden", !config.tokenRequired || Boolean(state.token));
    const status = await api("/api/status");
    els.lConnectValue.textContent = status.online ? `${t("online")} (${status.port || "-"})` : t("offline");
    setStatus(status.online ? t("onlinePort", status.port || "-") : status.message);
    await loadFanGroups();
    await loadDevices();
  } catch (error) {
    handleError(error);
  } finally {
    els.refreshButton.classList.remove("spinning");
    els.refreshButton.disabled = false;
  }
}

function openSettings() {
  els.settingsModal.classList.remove("hidden");
}

function closeSettings() {
  els.settingsModal.classList.add("hidden");
}

function renderConfig(config) {
  applyLanguage(config.language || "en");
  els.portValue.textContent = String(config.port || "-");
  els.pinModeValue.textContent = config.tokenRequired ? t("required") : t("off");
  els.usePinCheck.checked = Boolean(config.usePin ?? config.tokenRequired);
  els.urlList.innerHTML = "";

  for (const url of config.urls || []) {
    const row = document.createElement("a");
    row.href = url;
    row.textContent = url;
    els.urlList.appendChild(row);
  }
}

async function setUsePin() {
  const usePin = els.usePinCheck.checked;
  const token = els.settingsPinInput.value.trim() || state.token;
  if (usePin && !token) {
    els.usePinCheck.checked = false;
    showToast(t("enterPin"), true);
    return;
  }

  try {
    const result = await api("/api/config/access", {
      method: "POST",
      auth: false,
      body: JSON.stringify({ usePin, token })
    });
    els.pinModeValue.textContent = result.usePin ? t("required") : t("off");
    els.authPanel.classList.toggle("hidden", !result.usePin || Boolean(state.token));
    showToast(result.usePin ? t("pinEnabled") : t("pinDisabled"));
    await refreshAll();
  } catch (error) {
    els.usePinCheck.checked = !usePin;
    handleError(error);
  }
}

async function savePinSetting() {
  const token = els.settingsPinInput.value.trim();
  if (!token) {
    showToast(t("enterPin"), true);
    return;
  }

  try {
    state.token = token;
    localStorage.setItem("phoneControlToken", state.token);
    els.tokenInput.value = token;
    const result = await api("/api/config/access", {
      method: "POST",
      auth: false,
      body: JSON.stringify({ usePin: els.usePinCheck.checked, token })
    });
    els.pinModeValue.textContent = result.usePin ? t("required") : t("off");
    showToast(t("pinSaved"));
  } catch (error) {
    handleError(error);
  }
}

async function loadDevices() {
  const devices = await api("/api/devices");
  state.devices = devices;
  els.deviceSelect.innerHTML = "";

  for (const device of devices) {
    const option = document.createElement("option");
    option.value = device.id;
    option.textContent = device.name;
    els.deviceSelect.appendChild(option);
  }

  if (devices.length === 0) {
    els.themeGrid.innerHTML = "";
    els.themeCount.textContent = "0";
    setStatus(t("noDevice"));
    return;
  }

  state.selectedDeviceId = state.selectedDeviceId || devices[0].id;
  els.deviceSelect.value = state.selectedDeviceId;
  await handleTargetChanged();
}

async function loadFanGroups() {
  state.fanGroups = await api("/api/fan-groups");
  renderFanGroups();
}

async function loadEffects() {
  const target = selectedLightingTarget();
  if (target) {
    state.effects = await api(`${target}/lighting/effects${lightingQuery()}`);
  } else {
    state.effects = await api("/api/lighting/effects");
  }

  const current = await loadCurrentLightingState(target);
  if (current?.effect && state.effects.some(effect => effect.id === current.effect)) {
    state.selectedEffectId = current.effect;
  }

  if (!state.effects.some(effect => effect.id === state.selectedEffectId)) {
    state.selectedEffectId = state.effects[0]?.id || "";
  }

  renderEffects();
  if (current) {
    applyCurrentLightingState(current);
  }
}

async function loadCurrentLightingState(target) {
  if (!target || !isFanDeviceSelected()) {
    return null;
  }

  try {
    return await api(`${target}/lighting/current${lightingQuery()}`);
  } catch {
    return null;
  }
}

function lightingQuery() {
  return isFanDeviceSelected() ? `?merge=${state.fanMerge ? "true" : "false"}` : "";
}

function applyCurrentLightingState(current) {
  if (current.effect && state.effects.some(effect => effect.id === current.effect)) {
    state.selectedEffectId = current.effect;
    els.effectSelect.value = current.effect;
  }

  if (Number.isFinite(Number(current.brightness))) {
    const value = snapBrightness(current.brightness);
    els.ledBrightnessSlider.value = String(value);
    updateLedBrightnessUi(value);
  }

  if (Number.isFinite(Number(current.speed))) {
    state.selectedSpeed = snapBrightness(current.speed);
    renderSpeedSegments();
  }

  if (Number.isFinite(Number(current.direction))) {
    state.selectedDirection = Number(current.direction) ? 1 : 0;
    renderDirectionSegments();
  }

  syncEffectColor();
  if (Array.isArray(current.colors) && current.colors.length) {
    setColorInputValues(current.colors);
  }
}

async function handleTargetChanged() {
  const isFanTarget = isFanDeviceSelected();
  els.fanGroupPanel.classList.toggle("hidden", !isFanTarget);
  els.fanMergeRow.classList.toggle("hidden", !isFanTarget);
  els.applyAllFansButton.classList.toggle("hidden", !isFanTarget);
  els.applyEffectButton.textContent = isFanTarget ? t("applySelectedGroup") : t("applyLighting");
  els.fanMergeCheck.checked = state.fanMerge;
  els.screenCard.classList.toggle("hidden", isFanTarget);
  els.themesSection.classList.toggle("hidden", isFanTarget);
  if (!isFanTarget) {
    applyDeviceBrightness(selectedDevice());
  }

  if (isFanTarget) {
    if (!state.fanGroups.length) {
      await loadFanGroups();
    }

    renderFanGroups();
    await loadEffects();
    els.themeGrid.innerHTML = "";
    els.themeCount.textContent = "";
    setStatus(state.fanGroups.length ? t("fanReady") : t("noFanGroup"));
    return;
  }

  await loadEffects();
  await loadThemes();
}

function applyDeviceBrightness(device) {
  const value = Number(device?.screenBrightness ?? 50);
  const brightness = Number.isFinite(value) ? snapBrightness(value) : 50;
  els.brightnessSlider.value = String(brightness);
  updateBrightnessUi(brightness);
}

async function loadThemes() {
  if (!state.selectedDeviceId) {
    return;
  }

  els.themeGrid.innerHTML = "";
  els.themeCount.textContent = "";
  els.themesSection.classList.toggle("screen88Themes", selectedDevice()?.model === "universal-screen-8.8-inch");
  setStatus(t("loadingThemes"));
  const themes = await api(`/api/devices/${encodeURIComponent(state.selectedDeviceId)}/themes`);
  state.themes = themes;
  els.themeCount.textContent = String(themes.length);
  renderThemes();
  setStatus(themes.length ? t("ready") : t("noThemes"));
}

function renderFanGroups() {
  els.fanGroupSelect.innerHTML = "";

  for (const group of state.fanGroups) {
    const option = document.createElement("option");
    option.value = group.id;
    option.textContent = `${group.name} - ${group.ledCount} LEDs`;
    els.fanGroupSelect.appendChild(option);
  }

  state.selectedFanGroupId = state.selectedFanGroupId || state.fanGroups[0]?.id || "";
  els.fanGroupSelect.value = state.selectedFanGroupId;
}

function renderThemes() {
  els.themeGrid.innerHTML = "";

  for (const theme of state.themes) {
    const card = document.createElement("article");
    card.className = `themeCard${theme.isSelected ? " selected" : ""}`;

    const imageWrap = document.createElement("div");
    imageWrap.className = "themePreviewWrap";

    const img = document.createElement("img");
    img.className = "themePreview";
    img.alt = "";
    img.loading = "lazy";
    img.src = withToken(theme.previewUrl);
    imageWrap.appendChild(img);

    const button = document.createElement("button");
    button.className = "themeApply";
    button.textContent = theme.isSelected ? t("selected") : t("apply");
    button.disabled = theme.isSelected;
    button.addEventListener("click", () => applyTheme(theme.id, button));

    card.append(imageWrap, button);
    els.themeGrid.appendChild(card);
  }
}

function renderEffects() {
  els.effectSelect.innerHTML = "";

  for (const effect of state.effects) {
    const option = document.createElement("option");
    option.value = effect.id;
    option.textContent = effect.name;
    els.effectSelect.appendChild(option);
  }

  els.effectSelect.value = state.selectedEffectId;
  syncEffectColor();
}

async function applyTheme(themeId, button) {
  button.disabled = true;
  button.textContent = t("applying");
  try {
    const result = await api(`/api/devices/${encodeURIComponent(state.selectedDeviceId)}/apply`, {
      method: "POST",
      body: JSON.stringify({ themeId })
    });
    showToast(result.message || t("themeApplied"));
    await loadDevices();
  } catch (error) {
    button.disabled = false;
    button.textContent = t("apply");
    handleError(error);
  }
}

function onBrightnessInput() {
  const value = snapBrightness(els.brightnessSlider.value);
  els.brightnessSlider.value = String(value);
  updateBrightnessUi(value);
  window.clearTimeout(state.brightnessTimer);
  state.brightnessTimer = window.setTimeout(() => setBrightness(value), 180);
}

function onBrightnessPointer(event) {
  event.preventDefault();
  els.brightnessShell.setPointerCapture?.(event.pointerId);
  updateBrightnessFromPointer(event);

  const move = moveEvent => updateBrightnessFromPointer(moveEvent);
  const up = upEvent => {
    els.brightnessShell.releasePointerCapture?.(upEvent.pointerId);
    window.removeEventListener("pointermove", move);
    window.removeEventListener("pointerup", up);
  };

  window.addEventListener("pointermove", move);
  window.addEventListener("pointerup", up, { once: true });
}

function updateBrightnessFromPointer(event) {
  const rect = els.brightnessShell.getBoundingClientRect();
  const raw = 100 - ((event.clientY - rect.top) / rect.height) * 100;
  const value = snapBrightness(raw);
  if (Number(els.brightnessSlider.value) === value) {
    return;
  }

  els.brightnessSlider.value = String(value);
  updateBrightnessUi(value);
  window.clearTimeout(state.brightnessTimer);
  state.brightnessTimer = window.setTimeout(() => setBrightness(value), 120);
}

function updateBrightnessUi(value) {
  els.brightnessValue.textContent = `${value}%`;
  els.brightnessShell.style.setProperty("--brightness", `${value}%`);
  renderBrightnessSegments(els.brightnessSegments, value, setBrightnessFromSegment, true);
}

function onLedBrightnessInput() {
  const value = snapBrightness(els.ledBrightnessSlider.value);
  els.ledBrightnessSlider.value = String(value);
  updateLedBrightnessUi(value);
  window.clearTimeout(state.ledBrightnessTimer);
  state.ledBrightnessTimer = window.setTimeout(() => setLedBrightness(value), 180);
}

function updateLedBrightnessUi(value) {
  els.ledBrightnessValue.textContent = `${value}%`;
  els.ledBrightnessShell.style.setProperty("--level", `${value}%`);
  renderBrightnessSegments(els.ledBrightnessSegments, value, setLedBrightnessFromSegment);
}

function setBrightnessFromSegment(value) {
  els.brightnessSlider.value = String(value);
  updateBrightnessUi(value);
  setBrightness(value);
}

function setLedBrightnessFromSegment(value) {
  els.ledBrightnessSlider.value = String(value);
  updateLedBrightnessUi(value);
  setLedBrightness(value);
}

function snapBrightness(value) {
  return Math.max(0, Math.min(100, Math.round(Number(value) / 25) * 25));
}

function renderBrightnessSegments(container, selectedValue, onSelect, reverse = false) {
  container.innerHTML = "";
  const values = reverse ? [100, 75, 50, 25, 0] : [0, 25, 50, 75, 100];
  for (const value of values) {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = String(value);
    button.className = value === selectedValue ? "active" : "";
    button.addEventListener("click", () => onSelect(value));
    container.appendChild(button);
  }
}

function renderSpeedSegments() {
  els.speedSegments.innerHTML = "";
  els.speedValue.textContent = `${state.selectedSpeed}%`;
  for (const value of [0, 25, 50, 75, 100]) {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = String(value);
    button.className = value === state.selectedSpeed ? "active" : "";
    button.addEventListener("click", () => {
      state.selectedSpeed = value;
      renderSpeedSegments();
    });
    els.speedSegments.appendChild(button);
  }
}

function renderDirectionSegments() {
  els.directionSegments.innerHTML = "";
  const options = [
    { value: 0, label: t("forward") },
    { value: 1, label: t("reverse") }
  ];
  els.directionValue.textContent = options.find(option => option.value === state.selectedDirection)?.label || t("forward");
  for (const option of options) {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = option.label;
    button.className = option.value === state.selectedDirection ? "active" : "";
    button.addEventListener("click", () => {
      state.selectedDirection = option.value;
      renderDirectionSegments();
    });
    els.directionSegments.appendChild(button);
  }
}

async function setBrightness(value) {
  if (!state.selectedDeviceId || isFanDeviceSelected()) {
    return;
  }

  try {
    const result = await api(`/api/devices/${encodeURIComponent(state.selectedDeviceId)}/brightness`, {
      method: "POST",
      body: JSON.stringify({ value })
    });
    showToast(result.message || t("brightnessUpdated"));
  } catch (error) {
    handleError(error);
  }
}

async function setLedBrightness(value) {
  const target = selectedLightingTarget();
  if (!target) {
    return;
  }

  try {
    state.selectedEffectId = els.effectSelect.value || state.selectedEffectId;
    const result = await api(`${target}/lighting-effect`, {
      method: "POST",
      body: JSON.stringify({
        effect: state.selectedEffectId,
        brightness: value,
        color: selectedColors()[0],
        colors: selectedColors(),
        speed: state.selectedSpeed,
        direction: state.selectedDirection,
        merge: isFanDeviceSelected() && state.fanMerge
      })
    });
    showToast(t("ledBrightnessSet", value));
  } catch (error) {
    handleError(error);
  }
}

async function applyLightingEffect() {
  const target = selectedLightingTarget();
  if (!target) {
    return;
  }

  state.selectedEffectId = els.effectSelect.value || state.selectedEffectId;
  els.applyEffectButton.disabled = true;
  try {
    const result = await api(`${target}/lighting-effect`, {
      method: "POST",
      body: JSON.stringify({
        effect: state.selectedEffectId,
        brightness: Number(els.ledBrightnessSlider.value),
        color: selectedColors()[0],
        colors: selectedColors(),
        speed: state.selectedSpeed,
        direction: state.selectedDirection,
        merge: isFanDeviceSelected() && state.fanMerge
      })
    });
    showToast(result.message || t("lightingUpdated"));
  } catch (error) {
    handleError(error);
  } finally {
    els.applyEffectButton.disabled = false;
  }
}

async function applyLightingEffectToAllFans() {
  if (!isFanDeviceSelected() || !state.selectedFanGroupId) {
    return;
  }

  state.selectedEffectId = els.effectSelect.value || state.selectedEffectId;
  els.applyAllFansButton.disabled = true;
  try {
    const result = await api(`/api/fan-groups/${encodeURIComponent(state.selectedFanGroupId)}/lighting-effect`, {
      method: "POST",
      body: JSON.stringify({
        effect: state.selectedEffectId,
        brightness: Number(els.ledBrightnessSlider.value),
        color: selectedColors()[0],
        colors: selectedColors(),
        speed: state.selectedSpeed,
        direction: state.selectedDirection,
        merge: true,
        applyAll: true
      })
    });
    showToast(result.message || t("allFansUpdated"));
  } catch (error) {
    handleError(error);
  } finally {
    els.applyAllFansButton.disabled = false;
  }
}

function isFanDeviceSelected() {
  return selectedDevice()?.model === "l-wireless-fans";
}

function selectedDevice() {
  return state.devices.find(device => device.id === state.selectedDeviceId);
}

function selectedFanGroup() {
  return state.fanGroups.find(group => group.id === state.selectedFanGroupId);
}

function selectedLightingTarget() {
  if (isFanDeviceSelected()) {
    return state.selectedFanGroupId ? `/api/fan-groups/${encodeURIComponent(state.selectedFanGroupId)}` : "";
  }

  return state.selectedDeviceId ? `/api/devices/${encodeURIComponent(state.selectedDeviceId)}` : "";
}

function syncEffectColor() {
  const effect = state.effects.find(item => item.id === state.selectedEffectId);
  if (effect?.accent) {
    renderColorInputs(effect.accent);
    updateAccentFromColors();
  }
}

function colorSlotCount() {
  const deviceMax = selectedDevice()?.model === "universal-screen-8.8-inch" ? 6 : 4;
  const effect = state.effects.find(item => item.id === state.selectedEffectId);
  if (isFanDeviceSelected() && state.selectedEffectId === "static" && !state.fanMerge) {
    return 4;
  }

  return Math.min(deviceMax, effect?.colorCount ?? effectColorCount(state.selectedEffectId, deviceMax));
}

function effectColorCount(effectId, maxSlots) {
  const count = {
    "static": 1,
    "breathing": 2,
    "runway": 2,
    "meteor": 2,
    "stack": 2,
    "meteor-shower": 2,
    "tide": 2,
    "electric-current": 2,
    "warning": 2,
    "hourglass": 2,
    "echo": 2,
    "heartbeat": 2,
    "rainbow": maxSlots,
    "rainbow-morph": maxSlots,
    "color-cycle": maxSlots,
    "cover-cycle": maxSlots,
    "wave": maxSlots,
    "mop-up": maxSlots,
    "disco": maxSlots,
    "mixing": maxSlots,
    "paint": maxSlots,
    "snooker": maxSlots,
    "volume": maxSlots,
    "blow-up": maxSlots,
    "caterpillar": maxSlots,
    "lollipop": maxSlots,
    "sea-flow": maxSlots,
    "ripple": maxSlots,
    "twinkle": maxSlots
  }[effectId];
  return count || Math.min(2, maxSlots);
}

function renderColorInputs(seedColor) {
  const current = selectedColors();
  const effect = state.effects.find(item => item.id === state.selectedEffectId);
  const fallback = seedColor || effect?.accent || "#ff6b6b";
  const count = colorSlotCount();
  const palette = els.colorInputs.closest(".colorPalette");
  if (palette) {
    palette.classList.toggle("hidden", count === 0);
  }
  els.colorSlotHint.textContent = count === 1 ? t("colorCountOne") : t("colorCountMany", count);
  els.colorInputs.innerHTML = "";

  if (count === 0) {
    updateAccentFromColors();
    return;
  }

  for (let index = 0; index < count; index += 1) {
    const label = document.createElement("label");
    label.className = `colorChip${index === 0 ? " active" : ""}`;
    label.dataset.index = String(index);
    label.title = `Color ${index + 1}`;

    const input = document.createElement("input");
    input.type = "color";
    input.value = current[index] || fallback;

    const span = document.createElement("span");
    span.textContent = `C${index + 1}`;

    label.append(input, span);
    els.colorInputs.appendChild(label);
  }
}

function selectedColors() {
  return [...els.colorInputs.querySelectorAll("input[type='color']")].map(input => input.value);
}

function setColorInputValues(colors) {
  const inputs = [...els.colorInputs.querySelectorAll("input[type='color']")];
  if (!inputs.length) {
    return;
  }

  for (let index = 0; index < inputs.length; index += 1) {
    const color = normalizeHexColor(colors[index] || colors[colors.length - 1]);
    if (color) {
      inputs[index].value = color;
    }
  }

  updateAccentFromColors();
}

function normalizeHexColor(color) {
  return /^#[0-9a-fA-F]{6}$/.test(color || "") ? color : "";
}

function updateAccentFromColors() {
  els.ledBrightnessShell.style.setProperty("--accent", selectedColors()[0] || "#28c0b2");
}

function preventDoubleTapZoom() {
  let lastTouchEnd = 0;
  document.addEventListener("touchend", event => {
    const now = Date.now();
    if (now - lastTouchEnd <= 350) {
      event.preventDefault();
    }
    lastTouchEnd = now;
  }, { passive: false });
}

function setActiveColorSlot(index) {
  for (const chip of els.colorInputs.querySelectorAll(".colorChip")) {
    chip.classList.toggle("active", Number(chip.dataset.index) === index);
  }
}

function setActiveSlotColor(color) {
  const active = els.colorInputs.querySelector(".colorChip.active input") || els.colorInputs.querySelector("input[type='color']");
  if (!active) {
    return;
  }

  active.value = color;
  updateAccentFromColors();
}

function renderHexPalette() {
  const groups = [
    {
      label: "Main",
      colors: ["#ffffff", "#ff0000", "#ff8000", "#ffff00", "#00ff00", "#00ffff", "#0000ff", "#8000ff", "#ff00ff", "#ff0080"]
    },
    {
      label: "Accent",
      colors: ["#f2f2f7", "#808080", "#000000", "#ffd60a", "#64d2ff", "#5e5ce6", "#bf5af2", "#ff9f0a", "#30d158", "#ff6482"]
    }
  ];

  els.colorPalette.innerHTML = "";
  for (const group of groups) {
    const row = document.createElement("div");
    row.className = "hexPaletteRow";
    const label = document.createElement("span");
    label.textContent = group.label;
    row.appendChild(label);
    for (const color of group.colors) {
      const button = document.createElement("button");
      button.type = "button";
      button.dataset.color = color;
      button.title = color;
      button.style.setProperty("--swatch", color);
      row.appendChild(button);
    }
    els.colorPalette.appendChild(row);
  }
}

function saveToken() {
  state.token = els.tokenInput.value.trim();
  els.settingsPinInput.value = state.token;
  localStorage.setItem("phoneControlToken", state.token);
  els.authPanel.classList.add("hidden");
  refreshAll();
}

function forgetToken() {
  state.token = "";
  els.tokenInput.value = "";
  localStorage.removeItem("phoneControlToken");
  els.authPanel.classList.remove("hidden");
  showToast(t("pinCleared"));
}

async function api(url, options = {}) {
  const auth = options.auth !== false;
  const response = await fetch(url, {
    method: options.method || "GET",
    headers: {
      "Content-Type": "application/json",
      ...(auth && state.token ? { "X-PhoneControl-Token": state.token } : {})
    },
    body: options.body
  });

  if (response.status === 401) {
    els.authPanel.classList.remove("hidden");
    throw new Error(t("pinRequired"));
  }

  const text = await response.text();
  const data = text ? JSON.parse(text) : {};
  if (!response.ok) {
    throw new Error(data.message || data.Message || response.statusText);
  }

  return data;
}

function withToken(url) {
  return state.token ? `${url}${url.includes("?") ? "&" : "?"}t=${encodeURIComponent(state.token)}` : url;
}

function setStatus(message) {
  els.statusText.textContent = message;
}

function showToast(message, isError = false) {
  els.toast.textContent = message;
  els.toast.classList.toggle("error", isError);
  els.toast.classList.remove("hidden");
  window.clearTimeout(showToast.timer);
  showToast.timer = window.setTimeout(() => els.toast.classList.add("hidden"), 2600);
}

function handleError(error) {
  setStatus(error.message || t("requestFailed"));
  showToast(error.message || t("requestFailed"), true);
}
