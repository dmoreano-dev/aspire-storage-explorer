const API = '/api';
// Destructive requests must carry this header; the server rejects them otherwise.
const REQUEST_HEADERS = { 'X-Storage-Explorer': '1' };
const JSON_HEADERS = { ...REQUEST_HEADERS, 'Content-Type': 'application/json' };

const els = {
  app: document.querySelector('.app'),
  riskBanner: document.getElementById('risk-banner'),
  containers: document.getElementById('containers'),
  containerFilter: document.getElementById('container-filter'),
  containerFilterField: document.getElementById('container-filter-field'),
  containerCount: document.getElementById('container-count'),
  breadcrumb: document.getElementById('breadcrumb'),
  title: document.getElementById('title'),
  message: document.getElementById('message'),
  toolbar: document.getElementById('toolbar'),
  filter: document.getElementById('filter'),
  filterCount: document.getElementById('filter-count'),
  scopeButtons: document.querySelectorAll('.scope button'),
  listing: document.getElementById('listing'),
  sortHeaders: document.querySelectorAll('#entries th[data-sort]'),
  tbody: document.querySelector('#entries tbody'),
  status: document.getElementById('status'),
  statusSource: document.getElementById('status-source'),
  statusEndpoint: document.getElementById('status-endpoint'),
  statusPath: document.getElementById('status-path'),
  access: document.getElementById('access'),
  itemCount: document.getElementById('item-count'),
  refresh: document.getElementById('refresh'),
  theme: document.getElementById('theme'),
  confirmDialog: document.getElementById('confirm-dialog'),
  confirmText: document.getElementById('confirm-text'),
  confirmAccount: document.getElementById('confirm-account'),
  confirmContainer: document.getElementById('confirm-container'),
  confirmBlob: document.getElementById('confirm-blob'),
  confirmSize: document.getElementById('confirm-size'),
  confirmNameField: document.getElementById('confirm-name-field'),
  confirmNameHint: document.getElementById('confirm-name-hint'),
  confirmName: document.getElementById('confirm-name'),
  confirmCancel: document.getElementById('confirm-cancel'),
  confirmSubmit: document.getElementById('confirm-submit'),
  connectionAccount: document.getElementById('connection-account'),
  connectionKind: document.getElementById('connection-kind'),
  changeConnection: document.getElementById('change-connection'),
  connectionDialog: document.getElementById('connection-dialog'),
  connectionForm: document.getElementById('connection-form'),
  connectionString: document.getElementById('connection-string'),
  connectionShow: document.getElementById('connection-show'),
  connectionWrites: document.getElementById('connection-writes'),
  connectionAllowWrites: document.getElementById('connection-allow-writes'),
  connectionError: document.getElementById('connection-error'),
  connectionReset: document.getElementById('connection-reset'),
  connectionCancel: document.getElementById('connection-cancel'),
  connectionSubmit: document.getElementById('connection-submit'),
};

let connection = null;
let containers = [];
let renderToken = 0;
let statusTimer;

// The folder on screen, kept so that sorting and clearing the search do not need another request.
let listed = null;
let listedLocation = null;
// What the search box found, or null while the box is empty.
let found = null;
let searchToken = 0;
let searchTimer;
// 'folder' searches the folder on screen and everything below it, 'container' the whole container.
let searchScope = 'folder';
let sort = { key: 'name', direction: 1 };
// What must be typed to confirm a delete on an account that is not on this machine, or '' when nothing is asked.
let requiredName = '';

// Wait for a pause in typing before searching, since every search reads the listing from the account.
const SEARCH_DELAY_MS = 250;

const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });
const sortValues = {
  name: (entry) => entry.name,
  size: (entry) => entry.size,
  type: (entry) => entry.contentType,
  modified: (entry) => (entry.lastModified ? Date.parse(entry.lastModified) : null),
};

// --- Icons -------------------------------------------------------------------
// Static markup only: nothing that comes from the account is ever put in it.

const ICONS = {
  container: '<ellipse cx="12" cy="6" rx="8" ry="3"/><path d="M4 6v12c0 1.7 3.6 3 8 3s8-1.3 8-3V6"/><path d="M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3"/>',
  folder: '<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7z"/>',
  download: '<path d="M12 4v11"/><path d="m7 11 5 5 5-5"/><path d="M5 20h14"/>',
  trash: '<path d="M4 7h16"/><path d="M10 11v6M14 11v6"/><path d="M6 7l1 12a2 2 0 0 0 2 2h6a2 2 0 0 0 2-2l1-12"/><path d="M9 7V4h6v3"/>',
  search: '<circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/>',
  refresh: '<path d="M20 11a8 8 0 1 0-2.3 5.7"/><path d="M20 4v7h-7"/>',
  'chevron-down': '<path d="m6 9 6 6 6-6"/>',
  'chevron-right': '<path d="m9 6 6 6-6 6"/>',
  lock: '<rect x="5" y="11" width="14" height="9" rx="2"/><path d="M8 11V8a4 4 0 0 1 8 0v3"/>',
  unlock: '<rect x="5" y="11" width="14" height="9" rx="2"/><path d="M8 11V8a4 4 0 0 1 7.5-2"/>',
  alert: '<path d="M12 3 2 20h20L12 3z"/><path d="M12 10v4"/><path d="M12 17.5v.01"/>',
  layers: '<path d="M12 3 3 8l9 5 9-5-9-5z"/><path d="m3 13 9 5 9-5"/>',
  table: '<rect x="3" y="4" width="18" height="16" rx="2"/><path d="M3 10h18M9 4v16"/>',
  sun: '<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/>',
  moon: '<path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/>',
  monitor: '<rect x="3" y="4" width="18" height="12" rx="2"/><path d="M8 20h8M12 16v4"/>',
};

function icon(name) {
  const template = document.createElement('template');
  template.innerHTML =
    '<svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" ' +
    `stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${ICONS[name]}</svg>`;
  return template.content.firstElementChild;
}

// The page marks where an icon goes with <i data-icon="name">; the element is replaced and keeps its classes.
for (const placeholder of document.querySelectorAll('[data-icon]')) {
  const svg = icon(placeholder.dataset.icon);
  svg.classList.add(...placeholder.classList);
  placeholder.replaceWith(svg);
}

// --- Helpers -----------------------------------------------------------------

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function link(href, text, className) {
  const anchor = el('a', className, text);
  anchor.href = href;
  return anchor;
}

function safeDecode(value) {
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

function formatSize(bytes) {
  if (bytes == null) return '';
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB', 'TB'];
  let value = bytes / 1024;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  return `${value.toFixed(1)} ${units[unit]}`;
}

const timeFormat = new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit' });
const dayFormat = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' });
const dayYearFormat = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', year: 'numeric' });

// "Today, 9:14 AM", "Sep 24, 4:02 PM" or "Sep 24, 2025". The full date and time go in the tooltip.
function formatDate(value) {
  if (!value) return '';

  const date = new Date(value);
  const now = new Date();
  const startOfDay = (day) => new Date(day.getFullYear(), day.getMonth(), day.getDate()).getTime();
  const days = Math.round((startOfDay(now) - startOfDay(date)) / 86_400_000);

  if (days === 0) return `Today, ${timeFormat.format(date)}`;
  if (days === 1) return `Yesterday, ${timeFormat.format(date)}`;
  if (date.getFullYear() === now.getFullYear()) return `${dayFormat.format(date)}, ${timeFormat.format(date)}`;
  return dayYearFormat.format(date);
}

const TILE_BY_EXTENSION = {
  csv: 'sheet', tsv: 'sheet', xls: 'sheet', xlsx: 'sheet',
  pdf: 'doc',
  json: 'data', xml: 'data', yaml: 'data', yml: 'data',
  png: 'image', jpg: 'image', jpeg: 'image', gif: 'image', webp: 'image', svg: 'image', bmp: 'image', ico: 'image',
  md: 'text', markdown: 'text',
};

// The tile in front of a name: the extension on a color that tells the kind of file at a glance.
function renderTile(entry) {
  if (entry.isFolder) {
    const tile = el('span', 'tile tile-folder');
    tile.append(icon('folder'));
    return tile;
  }

  const match = /\.([A-Za-z0-9]{1,5})$/.exec(entry.name);
  const extension = match ? match[1].toLowerCase() : '';
  return el('span', `tile tile-${TILE_BY_EXTENSION[extension] ?? 'plain'}`, extension ? extension.toUpperCase() : 'FILE');
}

const plural = (count, noun) => `${count} ${noun}${count === 1 ? '' : 's'}`;

function showMessage(text, isError = false) {
  els.message.hidden = !text;
  els.message.textContent = text ?? '';
  els.message.className = isError ? 'message error' : 'message';
  els.message.setAttribute('role', isError ? 'alert' : 'status');
}

function setStatus(text) {
  els.status.textContent = text;
  clearTimeout(statusTimer);
  statusTimer = setTimeout(() => { els.status.textContent = ''; }, 5000);
}

// --- Location: kept in the URL hash as #/<container>/<prefix> -----------------

function parseLocation() {
  const raw = location.hash.replace(/^#\/?/, '');
  const slash = raw.indexOf('/');
  const container = safeDecode(slash === -1 ? raw : raw.slice(0, slash));
  const prefix = slash === -1 ? '' : raw.slice(slash + 1).split('/').map(safeDecode).join('/');
  return { container, prefix };
}

function hashFor(container, prefix = '') {
  const encodedPrefix = prefix.split('/').map(encodeURIComponent).join('/');
  return `#/${encodeURIComponent(container)}/${encodedPrefix}`;
}

// --- API -----------------------------------------------------------------------

async function api(url, options) {
  const response = await fetch(url, options);
  if (!response.ok) {
    let detail = response.statusText;
    try {
      const problem = await response.json();
      detail = problem.detail || problem.title || detail;
    } catch {
      // The body is not JSON; keep the status text.
    }
    throw new Error(`${response.status} ${detail}`.trim());
  }
  return response;
}

async function getJson(url) {
  return (await api(url)).json();
}

const containerUrl = (container) => `${API}/containers/${encodeURIComponent(container)}`;
const blobUrl = (container, path) => `${containerUrl(container)}/blob?path=${encodeURIComponent(path)}`;

// --- Rendering -----------------------------------------------------------------

const containerTerm = () => els.containerFilter.value.trim();

function matchingContainers() {
  const needle = containerTerm().toLowerCase();
  return containers.filter((container) => container.name.toLowerCase().includes(needle));
}

function renderContainers(active) {
  const matching = matchingContainers();
  const items = matching.map((container) => {
    const item = el('li');
    const anchor = link(hashFor(container.name));
    anchor.title = container.name;
    anchor.append(icon('container'), el('span', undefined, container.name));
    if (container.name === active) anchor.setAttribute('aria-current', 'page');
    item.append(anchor);
    return item;
  });

  if (containers.length > 0 && matching.length === 0) items.push(el('li', 'empty', 'No containers match.'));

  els.containerFilterField.hidden = containers.length === 0;
  els.containerCount.textContent = containers.length > 0 ? String(containers.length) : '';
  els.containers.replaceChildren(...items);
}

// The last segment of the path is the title; the ones before it are the breadcrumb.
function renderLocation(container, prefix) {
  const segments = prefix.split('/').filter(Boolean);
  const crumbs = [];
  let title = 'Containers';

  if (container) {
    crumbs.push(link('#/', 'Containers'));

    if (segments.length === 0) {
      title = container;
    } else {
      crumbs.push(link(hashFor(container), container));

      let accumulated = '';
      segments.forEach((segment, index) => {
        accumulated += `${segment}/`;
        if (index < segments.length - 1) crumbs.push(link(hashFor(container, accumulated), segment));
      });
      title = segments[segments.length - 1];
    }
  }

  els.breadcrumb.replaceChildren(
    ...crumbs.flatMap((crumb, index) => (index === 0 ? [crumb] : [icon('chevron-right'), crumb])),
  );
  els.title.textContent = title;
  els.statusPath.textContent = container ? [container, ...segments].join('/') : '';
}

function hideListing() {
  listed = null;
  found = null;
  els.listing.hidden = true;
  els.toolbar.hidden = true;
  els.itemCount.textContent = '';
}

// Folders stay on top whatever the column and the direction. They have no size, type or date, so they go by name.
function compareEntries(a, b) {
  if (a.isFolder !== b.isFolder) return a.isFolder ? -1 : 1;

  const key = a.isFolder ? 'name' : sort.key;
  const direction = key === sort.key ? sort.direction : 1;
  const left = sortValues[key](a);
  const right = sortValues[key](b);
  const byName = collator.compare(a.name, b.name);

  if (left == null || right == null) {
    // A missing value goes last in both directions.
    return left == null && right == null ? byName : left == null ? 1 : -1;
  }

  const result = typeof left === 'number' ? left - right : collator.compare(left, right);
  return result * direction || byName;
}

function renderSortHeaders() {
  for (const header of els.sortHeaders) {
    const active = header.dataset.sort === sort.key;
    if (active) {
      header.setAttribute('aria-sort', sort.direction === 1 ? 'ascending' : 'descending');
    } else {
      header.removeAttribute('aria-sort');
    }
    header.querySelector('.arrow').textContent = active ? (sort.direction === 1 ? '↑' : '↓') : '';
  }
}

function sortBy(key) {
  sort = { key, direction: sort.key === key ? -sort.direction : 1 };
  renderEntries();
}

const currentTerm = () => els.filter.value.trim();

function renderScope() {
  for (const button of els.scopeButtons) {
    button.setAttribute('aria-pressed', String(button.dataset.scope === searchScope));
  }
}

// Shows the folder, or what the search box found.
function renderEntries() {
  if (!listed) return;

  const { container, prefix, listing: folder } = listed;
  const listing = found ? found.listing : folder;
  const entries = [...listing.entries].sort(compareEntries);

  els.tbody.replaceChildren(...entries.map((entry) => renderRow(container, entry)));
  renderSortHeaders();

  els.listing.hidden = entries.length === 0;
  els.toolbar.hidden = folder.entries.length === 0;
  els.filterCount.textContent = found ? `${entries.length} found` : '';
  els.itemCount.textContent = found ? `${entries.length} found` : plural(entries.length, 'item');

  if (folder.entries.length === 0) {
    showMessage(prefix ? 'This folder is empty.' : 'This container is empty.');
  } else if (found) {
    const limit = listing.truncated ? 'The search stopped at its limit, so there may be more matches.' : '';
    const where = found.scope === 'container' ? 'this container' : 'this folder or below';
    showMessage(entries.length === 0 ? `No blobs match "${found.term}" in ${where}. ${limit}`.trim() : limit);
  } else if (listing.truncated) {
    showMessage(`Listing truncated: showing the first ${entries.length} entries.`);
  } else {
    showMessage('');
  }
}

// Looks for the text of the search box in the folder on screen and below it, or in the whole container.
async function search() {
  if (!listed) return;

  const token = ++searchToken;
  const term = currentTerm();
  if (!term) {
    found = null;
    renderEntries();
    return;
  }

  const { container, prefix } = listed;
  const scope = searchScope;
  els.filterCount.textContent = 'Searching…';
  try {
    const searchPrefix = scope === 'container' ? '' : prefix;
    const listing = await getJson(
      `${containerUrl(container)}/search?prefix=${encodeURIComponent(searchPrefix)}&q=${encodeURIComponent(term)}`);
    if (token !== searchToken) return;
    found = { term, scope, listing };
    renderEntries();
  } catch (error) {
    if (token !== searchToken) return;
    found = null;
    renderEntries();
    showMessage(`Could not search "${container}": ${error.message}`, true);
  }
}

function renderRow(container, entry) {
  const row = el('tr');

  const name = el('td');
  const cell = el('div', 'name-cell');
  const text = el('span', 'name-text');
  if (entry.isFolder) {
    text.append(link(hashFor(container, entry.path), entry.name, 'folder'));
  } else {
    // A search result is named by its path below the folder searched: the folder part links to where the blob is.
    const slash = entry.name.lastIndexOf('/');
    if (slash !== -1) {
      const folder = entry.path.slice(0, entry.path.length - entry.name.length + slash + 1);
      text.append(link(hashFor(container, folder), entry.name.slice(0, slash + 1), 'path'));
    }
    text.append(entry.name.slice(slash + 1));
  }
  cell.append(renderTile(entry), text);
  name.append(cell);

  const type = el('td', 'type', entry.isFolder ? 'Folder' : entry.contentType ?? '');
  const size = el('td', entry.isFolder ? 'num none' : 'num', entry.isFolder ? '—' : formatSize(entry.size));
  const modified = el('td', entry.isFolder ? 'modified none' : 'modified', entry.isFolder ? '—' : formatDate(entry.lastModified));
  if (entry.lastModified) modified.title = new Date(entry.lastModified).toLocaleString();

  const actions = el('td');
  const group = el('div', 'row-actions');
  if (!entry.isFolder) {
    const download = link(blobUrl(container, entry.path), undefined, 'icon-button outlined');
    download.title = 'Download';
    download.setAttribute('aria-label', `Download ${entry.name}`);
    download.append(icon('download'));
    group.append(download);

    // Hiding the button is only a courtesy: the server refuses the delete on a read-only connection anyway.
    if (connection && !connection.readOnly) {
      const deleteButton = el('button', 'icon-button danger');
      deleteButton.type = 'button';
      deleteButton.title = 'Delete';
      deleteButton.setAttribute('aria-label', `Delete ${entry.name}`);
      deleteButton.append(icon('trash'));
      deleteButton.addEventListener('click', () => deleteBlob(container, entry));
      group.append(deleteButton);
    }
  }
  actions.append(group);

  row.append(name, type, size, modified, actions);
  return row;
}

async function render() {
  const token = ++renderToken;
  const { container, prefix } = parseLocation();

  // A search in flight is about a listing that is being replaced.
  searchToken++;
  clearTimeout(searchTimer);

  renderContainers(container);
  renderLocation(container, prefix);

  // A search applies to the folder it was typed in.
  const locationKey = `${container}/${prefix}`;
  if (locationKey !== listedLocation) {
    listedLocation = locationKey;
    els.filter.value = '';
  }

  if (!container) {
    hideListing();
    showMessage(containers.length === 0 ? 'No containers found.' : 'Select a container to browse its blobs.');
    return;
  }

  try {
    const listing = await getJson(`${containerUrl(container)}/entries?prefix=${encodeURIComponent(prefix)}`);
    if (token !== renderToken) return;
    listed = { container, prefix, listing };
    found = null;

    // After a refresh or a delete the search box may still have text: search again.
    if (currentTerm()) await search();
    else renderEntries();
  } catch (error) {
    if (token !== renderToken) return;
    hideListing();
    showMessage(`Could not list "${container}": ${error.message}`, true);
  }
}

// The state of the connection is told in one place, the status bar, and by color everywhere else:
// green for a local account, blue for a remote one that is read-only, red for a remote one that can be changed.
function renderConnection() {
  const readOnly = connection?.readOnly ?? false;
  const risky = connection != null && !readOnly && !connection.isLocal;
  const tone = connection == null ? 'unknown' : risky ? 'danger' : connection.isLocal ? 'ok' : 'info';

  els.app.dataset.tone = tone;
  els.riskBanner.hidden = !risky;

  els.connectionAccount.textContent = connection ? connection.accountName : 'Connection';
  els.connectionKind.textContent = connection ? (risky ? 'Writable · own risk' : connection.isLocal ? 'Local' : 'Remote') : '';
  els.changeConnection.title = connection
    ? `${connection.endpoint}${connection.isCustom ? ' (custom connection)' : ''}. Change connection`
    : 'Change connection';

  els.statusSource.textContent = connection?.isCustom ? 'Custom connection, kept in memory' : 'AppHost connection';
  els.statusEndpoint.textContent = connection?.endpoint ?? '';

  els.access.hidden = connection == null;
  if (connection) {
    els.access.className = risky ? 'access danger' : readOnly ? 'access' : 'access ok';
    els.access.replaceChildren(
      icon(readOnly ? 'lock' : 'unlock'),
      el('span', undefined, readOnly ? 'Read-only' : connection.isLocal ? 'Read & write' : 'Writable · own risk'),
    );
    if (readOnly) {
      els.access.title = connection.readOnlyLocked
        ? 'readOnly is set in WithStorageExplorer.'
        : 'This account is not on this machine. Use Change connection and allow changes to delete.';
    } else {
      els.access.title = connection.isLocal
        ? 'Deleting is enabled.'
        : 'This account is not on this machine and changes are allowed.';
    }
  }
}

async function loadConnection() {
  try {
    connection = await getJson(`${API}/connection`);
  } catch {
    connection = null;
  }
  renderConnection();
}

async function loadContainers() {
  try {
    containers = await getJson(`${API}/containers`);
  } catch (error) {
    containers = [];
    showMessage(`Could not load containers: ${error.message}`, true);
  }
}

// --- Actions -------------------------------------------------------------------

// Asks before a delete. On an account that is not on this machine the name of the blob must be typed as well.
function confirmDelete(container, entry) {
  const remote = connection != null && !connection.isLocal;
  const fileName = entry.path.slice(entry.path.lastIndexOf('/') + 1);

  els.confirmText.textContent = remote
    ? 'It will be removed from an account that is not on this machine. This cannot be undone.'
    : 'It will be removed from the account below. This cannot be undone.';

  const account = el('span', undefined, connection?.accountName ?? '');
  els.confirmAccount.replaceChildren(...(remote ? [account, el('span', 'tag', 'Remote')] : [account]));
  els.confirmContainer.textContent = container;
  els.confirmBlob.textContent = entry.path;
  els.confirmSize.textContent = formatSize(entry.size);

  requiredName = remote ? fileName : '';
  els.confirmNameField.hidden = !remote;
  els.confirmNameHint.textContent = fileName;
  els.confirmName.value = '';
  els.confirmSubmit.disabled = remote;

  els.confirmDialog.returnValue = '';

  return new Promise((resolve) => {
    els.confirmDialog.addEventListener('close', () => resolve(els.confirmDialog.returnValue === 'confirm'), { once: true });
    els.confirmDialog.showModal();
    (remote ? els.confirmName : els.confirmCancel).focus();
  });
}

async function deleteBlob(container, entry) {
  const confirmed = await confirmDelete(container, entry);
  if (!confirmed) return;

  try {
    await api(blobUrl(container, entry.path), { method: 'DELETE', headers: REQUEST_HEADERS });
    setStatus(`Deleted ${entry.path}`);
    await render();
  } catch (error) {
    showMessage(`Could not delete "${entry.path}": ${error.message}`, true);
  }
}

async function refresh() {
  await Promise.all([loadConnection(), loadContainers()]);
  await render();
}

els.confirmCancel.addEventListener('click', () => els.confirmDialog.close('cancel'));
els.confirmName.addEventListener('input', () => {
  els.confirmSubmit.disabled = els.confirmName.value !== requiredName;
});

// --- Connection dialog ---------------------------------------------------------

function showConnectionError(text) {
  els.connectionError.hidden = !text;
  els.connectionError.textContent = text ?? '';
}

function setConnectionBusy(busy) {
  els.connectionSubmit.disabled = busy;
  els.connectionReset.disabled = busy;
  els.connectionSubmit.textContent = busy ? 'Connecting…' : 'Connect';
}

function openConnectionDialog() {
  showConnectionError('');
  els.connectionShow.checked = false;
  els.connectionString.type = 'password';
  els.connectionAllowWrites.checked = false;
  els.connectionWrites.hidden = connection?.readOnlyLocked ?? false;
  els.connectionReset.hidden = !connection?.isCustom;
  els.connectionDialog.showModal();
  els.connectionString.focus();
}

// The account changed, so the containers and the current path no longer apply.
async function connectionChanged(info) {
  connection = info;
  els.containerFilter.value = '';
  els.connectionDialog.close();
  history.replaceState(null, '', '#/');
  setStatus(`Connected to ${info.accountName}`);
  await refresh();
}

async function sendConnection(options) {
  setConnectionBusy(true);
  showConnectionError('');
  try {
    const response = await api(`${API}/connection`, options);
    await connectionChanged(await response.json());
  } catch (error) {
    showConnectionError(error.message);
  } finally {
    setConnectionBusy(false);
  }
}

els.changeConnection.addEventListener('click', openConnectionDialog);
els.connectionCancel.addEventListener('click', () => els.connectionDialog.close());
els.connectionShow.addEventListener('change', () => {
  els.connectionString.type = els.connectionShow.checked ? 'text' : 'password';
});
// The secret must not linger in the page once the dialog closes, however it closes, and neither must the consent to
// write: it is asked again for every connection.
els.connectionDialog.addEventListener('close', () => {
  els.connectionString.value = '';
  els.connectionString.type = 'password';
  els.connectionAllowWrites.checked = false;
});
els.connectionForm.addEventListener('submit', (event) => {
  event.preventDefault();
  sendConnection({
    method: 'PUT',
    headers: JSON_HEADERS,
    body: JSON.stringify({
      connectionString: els.connectionString.value,
      allowWrites: els.connectionAllowWrites.checked,
    }),
  });
});
els.connectionReset.addEventListener('click', () => sendConnection({ method: 'DELETE', headers: REQUEST_HEADERS }));

// --- Theme ---------------------------------------------------------------------

// System follows the theme of the operating system; Light and Dark override it. index.html applies the saved one
// before the first paint, by setting data-theme on <html>; this is where it is changed.
const THEMES = ['system', 'light', 'dark'];
const THEME_LABELS = { system: 'System', light: 'Light', dark: 'Dark' };
const THEME_ICONS = { system: 'monitor', light: 'sun', dark: 'moon' };
const THEME_COOKIE = 'storage-explorer-theme';

let theme = THEMES.includes(document.documentElement.dataset.theme) ? document.documentElement.dataset.theme : 'system';

const nextTheme = () => THEMES[(THEMES.indexOf(theme) + 1) % THEMES.length];

function renderTheme() {
  if (theme === 'system') delete document.documentElement.dataset.theme;
  else document.documentElement.dataset.theme = theme;

  const label = `Theme: ${THEME_LABELS[theme]}. Click for ${THEME_LABELS[nextTheme()]}`;
  els.theme.replaceChildren(icon(THEME_ICONS[theme]));
  els.theme.title = label;
  els.theme.setAttribute('aria-label', label);
}

// A cookie and not localStorage: Aspire gives the explorer a different port on each run, and localStorage is kept per
// port, while a cookie is shared by every port of localhost, so the choice survives a restart. It holds nothing else.
function saveTheme() {
  // "System" is no choice at all, so it removes the cookie.
  const cookie = theme === 'system' ? `${THEME_COOKIE}=; max-age=0` : `${THEME_COOKIE}=${theme}; max-age=31536000`;
  try {
    document.cookie = `${cookie}; path=/; SameSite=Lax`;
  } catch {
    // Cookies are blocked: the choice lasts until the page is reloaded.
  }
}

els.theme.addEventListener('click', () => {
  theme = nextTheme();
  renderTheme();
  saveTheme();
  setStatus(`Theme: ${THEME_LABELS[theme]}`);
});

// --- Search, sort and keyboard -------------------------------------------------

els.containerFilter.addEventListener('input', () => renderContainers(parseLocation().container));
els.containerFilter.addEventListener('keydown', (event) => {
  // Enter opens the first container that matches.
  const [first] = matchingContainers();
  if (event.key === 'Enter' && containerTerm() && first) location.hash = hashFor(first.name);
});

// "/" jumps to the container search, as it does in most tools with a list on the side.
document.addEventListener('keydown', (event) => {
  if (event.key !== '/' || event.metaKey || event.ctrlKey || event.altKey) return;
  if (event.target instanceof HTMLElement && event.target.closest('input, textarea, select, [contenteditable]')) return;
  if (document.querySelector('dialog[open]') || els.containerFilterField.hidden) return;

  event.preventDefault();
  els.containerFilter.focus();
});

els.filter.addEventListener('input', () => {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(search, currentTerm() ? SEARCH_DELAY_MS : 0);
});
for (const button of els.scopeButtons) {
  button.addEventListener('click', () => {
    if (searchScope === button.dataset.scope) return;
    searchScope = button.dataset.scope;
    renderScope();
    if (currentTerm()) search();
  });
}
for (const header of els.sortHeaders) {
  header.querySelector('button').addEventListener('click', () => sortBy(header.dataset.sort));
}

els.refresh.addEventListener('click', refresh);
window.addEventListener('hashchange', render);
renderScope();
renderTheme();
refresh();
