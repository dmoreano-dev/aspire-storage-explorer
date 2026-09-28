const API = '/api';
// Destructive requests must carry this header; the server rejects them otherwise.
const REQUEST_HEADERS = { 'X-Storage-Explorer': '1' };
const JSON_HEADERS = { ...REQUEST_HEADERS, 'Content-Type': 'application/json' };

const els = {
  app: document.querySelector('.app'),
  riskBanner: document.getElementById('risk-banner'),
  serviceButtons: document.querySelectorAll('.service-switch button'),
  sidebarNav: document.getElementById('sidebar-nav'),
  listTitle: document.getElementById('list-title'),
  containers: document.getElementById('containers'),
  containerFilter: document.getElementById('container-filter'),
  containerFilterField: document.getElementById('container-filter-field'),
  containerCount: document.getElementById('container-count'),
  newContainerButton: document.getElementById('new-container-button'),
  filterShortcutHint: document.getElementById('filter-shortcut-hint'),
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
  panel: document.querySelector('.panel'),
  blobActions: document.getElementById('blob-actions'),
  newFolderButton: document.getElementById('new-folder-button'),
  uploadButton: document.getElementById('upload-button'),
  uploadInput: document.getElementById('upload-input'),
  dropOverlay: document.getElementById('drop-overlay'),
  queueToolbar: document.getElementById('queue-toolbar'),
  queueFilter: document.getElementById('queue-filter'),
  queueFilterCount: document.getElementById('queue-filter-count'),
  deletePeekedMessages: document.getElementById('delete-peeked-messages'),
  clearQueue: document.getElementById('clear-queue'),
  queueNote: document.getElementById('queue-note'),
  queueListing: document.getElementById('queue-listing'),
  messagesBody: document.querySelector('#messages tbody'),
  tableToolbar: document.getElementById('table-toolbar'),
  tableFilter: document.getElementById('table-filter'),
  tableFilterClear: document.getElementById('table-filter-clear'),
  tableFilterCount: document.getElementById('table-filter-count'),
  tableListing: document.getElementById('table-listing'),
  entitiesHeadRow: document.querySelector('#entities thead tr'),
  entitiesBody: document.querySelector('#entities tbody'),
  loadMoreBar: document.getElementById('load-more-bar'),
  loadMoreSummary: document.getElementById('load-more-summary'),
  loadMore: document.getElementById('load-more'),
  status: document.getElementById('status'),
  statusSource: document.getElementById('status-source'),
  statusEndpoint: document.getElementById('status-endpoint'),
  statusPath: document.getElementById('status-path'),
  access: document.getElementById('access'),
  itemCount: document.getElementById('item-count'),
  refresh: document.getElementById('refresh'),
  theme: document.getElementById('theme'),
  confirmDialog: document.getElementById('confirm-dialog'),
  confirmTitle: document.getElementById('confirm-title'),
  confirmText: document.getElementById('confirm-text'),
  confirmDetails: document.getElementById('confirm-details'),
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
  promptDialog: document.getElementById('prompt-dialog'),
  promptForm: document.getElementById('prompt-form'),
  promptTitle: document.getElementById('prompt-title'),
  promptLabel: document.getElementById('prompt-label'),
  promptInput: document.getElementById('prompt-input'),
  promptError: document.getElementById('prompt-error'),
  promptCancel: document.getElementById('prompt-cancel'),
  promptSubmit: document.getElementById('prompt-submit'),
  previewDialog: document.getElementById('preview-dialog'),
  previewTitle: document.getElementById('preview-title'),
  previewMeta: document.getElementById('preview-meta'),
  previewBody: document.getElementById('preview-body'),
  previewDownload: document.getElementById('preview-download'),
  previewClose: document.getElementById('preview-close'),
};

let connection = null;
// Which service the sidebar and panel show: 'blobs', 'queues' or 'tables'.
let service = 'blobs';
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
// How many nested dragenter events (the listing, a row, ...) are unmatched by a dragleave yet; the drop overlay shows
// while this is above zero, so moving over a child element does not flicker it on and off.
let dragDepth = 0;

// --- Queues state --------------------------------------------------------------

let queues = [];
// The queue on screen, and the messages peeked from it (null before they load, or when none is selected).
let activeQueue = null;
let queueMessages = null;
// Which messages (by id) have their detail row open.
let expandedMessages = new Set();

// --- Tables state --------------------------------------------------------------

let tables = [];
// The table on screen, its entities loaded so far (across every "Load more"), the columns to show them under (the
// union of the keys seen so far, kept in the same order the server puts them in) and the token for the next page,
// or null when there isn't one.
let activeTable = null;
let tableRows = [];
let tableColumns = [];
let tableContinuationToken = null;
// Whether "Load more" has appended a further page since the last fresh query: while true, polling leaves the table
// alone rather than throwing that state away with a first-page-only refresh.
let tableLoadedMore = false;

// Wait for a pause in typing before searching, since every search reads the listing from the account.
const SEARCH_DELAY_MS = 250;

// How often a background poll asks the active view's data again, to notice a write made from outside this tab (the
// CLI, Azure Storage Explorer, another process). See the "Auto-refresh" section below.
const POLL_INTERVAL_MS = 8_000;

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
  info: '<circle cx="12" cy="12" r="9"/><path d="M12 8v5"/><path d="M12 16.5v.01"/>',
  play: '<path d="M7 4.5v15a1 1 0 0 0 1.5.9l12-7.5a1 1 0 0 0 0-1.8l-12-7.5A1 1 0 0 0 7 4.5z"/>',
  eye: '<path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8Z"/><circle cx="12" cy="12" r="3"/>',
  upload: '<path d="M12 20V9"/><path d="m7 13 5-5 5 5"/><path d="M5 4h14"/>',
  plus: '<path d="M12 5v14"/><path d="M5 12h14"/>',
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

// Preview support: images, PDF, JSON and text. Kept apart from TILE_BY_EXTENSION above since that one is only
// about the tile's color and label, not what can be shown in the preview dialog.
const PREVIEW_KIND_BY_EXTENSION = {
  png: 'image', jpg: 'image', jpeg: 'image', gif: 'image', webp: 'image', svg: 'image', bmp: 'image', ico: 'image',
  pdf: 'pdf',
  json: 'json',
  txt: 'text', log: 'text', md: 'text', markdown: 'text', yaml: 'text', yml: 'text', xml: 'text', csv: 'text',
  ini: 'text', conf: 'text',
};

// The blob's own content type is trusted first, since it reflects what was actually set when the blob was
// written; the extension is only a fallback for a missing or generic one (e.g. "application/octet-stream").
function previewKindOf(entry) {
  if (entry.isFolder) return null;

  const contentType = (entry.contentType ?? '').toLowerCase().split(';')[0].trim();
  if (contentType.startsWith('image/')) return 'image';
  if (contentType === 'application/pdf') return 'pdf';
  if (contentType === 'application/json') return 'json';
  if (contentType.startsWith('text/')) return 'text';

  const match = /\.([A-Za-z0-9]{1,9})$/.exec(entry.name);
  return match ? PREVIEW_KIND_BY_EXTENSION[match[1].toLowerCase()] ?? null : null;
}

// Above this, the preview dialog shows a "too large" message with a Download link instead of fetching or
// embedding the content.
const MAX_PREVIEW_BYTES = 15 * 1024 * 1024;

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

// --- Location: kept in the URL hash -------------------------------------------
// Blobs: #/<container>/<prefix>. Queues: #queues or #queues/<queue>. Tables: #tables or #tables/<table>. Anything
// else falls back to blobs.

function parseHash() {
  const raw = location.hash.replace(/^#\/?/, '');

  for (const service of ['queues', 'tables']) {
    if (raw === service || raw.startsWith(`${service}/`)) {
      const name = raw === service ? '' : safeDecode(raw.slice(service.length + 1));
      return { service, name };
    }
  }

  const slash = raw.indexOf('/');
  const container = safeDecode(slash === -1 ? raw : raw.slice(0, slash));
  const prefix = slash === -1 ? '' : raw.slice(slash + 1).split('/').map(safeDecode).join('/');
  return { service: 'blobs', container, prefix };
}

function hashFor(container, prefix = '') {
  const encodedPrefix = prefix.split('/').map(encodeURIComponent).join('/');
  return `#/${encodeURIComponent(container)}/${encodedPrefix}`;
}

const hashForQueue = (queue) => (queue ? `#queues/${encodeURIComponent(queue)}` : '#queues');
const hashForTable = (table) => (table ? `#tables/${encodeURIComponent(table)}` : '#tables');

// The hash for whatever is the active service's sidebar list, given the name of one of its items.
function hashForItem(name) {
  if (service === 'queues') return hashForQueue(name);
  if (service === 'tables') return hashForTable(name);
  return hashFor(name);
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

const containerUrl = (container) => `${API}/blobs/containers/${encodeURIComponent(container)}`;
const blobUrl = (container, path) => `${containerUrl(container)}/blob?path=${encodeURIComponent(path)}`;
const previewUrl = (container, path) => `${blobUrl(container, path)}&inline=true`;
const folderUrl = (container, path) => `${containerUrl(container)}/folder?path=${encodeURIComponent(path)}`;

const queuesUrl = `${API}/queues`;
const queueUrl = (queue) => `${queuesUrl}/${encodeURIComponent(queue)}`;
const queueMessagesUrl = (queue) => `${queueUrl(queue)}/messages`;
const queuePeekedMessagesUrl = (queue) => `${queueMessagesUrl(queue)}/peeked`;

const tablesUrl = `${API}/tables`;
const tableUrl = (table) => `${tablesUrl}/${encodeURIComponent(table)}`;
function tableEntitiesUrl(table, filter, continuationToken) {
  const params = new URLSearchParams();
  if (filter) params.set('filter', filter);
  if (continuationToken) params.set('continuationToken', continuationToken);

  const query = params.toString();
  return `${tableUrl(table)}/entities${query ? `?${query}` : ''}`;
}
const entityUrl = (table, partitionKey, rowKey) =>
  `${tableUrl(table)}/entities?partitionKey=${encodeURIComponent(partitionKey)}&rowKey=${encodeURIComponent(rowKey)}`;

// --- Rendering -----------------------------------------------------------------

// The service switch decides what the sidebar list is a list of: containers, queues or tables. All three are named
// things with a client-side substring filter, so one set of functions renders any of them, reusing the same markup.

const SIDEBAR_LABELS = {
  blobs: { heading: 'Containers', placeholder: 'Search containers', empty: 'No containers match.', icon: 'container' },
  queues: { heading: 'Queues', placeholder: 'Search queues', empty: 'No queues match.', icon: 'layers' },
  tables: { heading: 'Tables', placeholder: 'Search tables', empty: 'No tables match.', icon: 'table' },
};

// The services besides Blobs (always there), and what says whether the account has them.
const OPTIONAL_SERVICES = {
  queues: 'hasQueues',
  tables: 'hasTables',
};

const sidebarItems = () => (service === 'queues' ? queues : service === 'tables' ? tables : containers);
const sidebarTerm = () => els.containerFilter.value.trim();

function matchingSidebarItems() {
  const needle = sidebarTerm().toLowerCase();
  return sidebarItems().filter((item) => item.name.toLowerCase().includes(needle));
}

function renderServiceSwitch() {
  for (const button of els.serviceButtons) {
    button.setAttribute('aria-pressed', String(button.dataset.service === service));

    const hasKey = OPTIONAL_SERVICES[button.dataset.service];
    if (!hasKey) continue; // Blobs is always there.

    // The value has to be the string "true" and not just present: that is what the CSS and the click handler below
    // check for.
    const has = connection?.[hasKey] ?? false;
    if (has) button.removeAttribute('aria-disabled');
    else button.setAttribute('aria-disabled', 'true');
    button.title = has || !connection ? '' : `This account has no ${button.dataset.service.slice(0, -1)} endpoint`;
  }
}

function renderSidebarHeading() {
  const labels = SIDEBAR_LABELS[service];
  els.sidebarNav.setAttribute('aria-label', labels.heading);
  els.listTitle.textContent = labels.heading;
  els.containerFilter.placeholder = labels.placeholder;
  els.containerFilter.setAttribute('aria-label', labels.placeholder);
  els.filterShortcutHint.title = `Press / to ${labels.placeholder.toLowerCase()}`;
  // Hiding the button is only a courtesy: the server refuses the create on a read-only connection anyway.
  els.newContainerButton.hidden = !(service === 'blobs' && connection && !connection.readOnly);
}

function renderSidebarList(active) {
  const labels = SIDEBAR_LABELS[service];
  const items = sidebarItems();
  const matching = matchingSidebarItems();
  const rows = matching.map((item) => {
    const row = el('li');
    const anchor = link(hashForItem(item.name));
    anchor.title = item.name;
    anchor.append(icon(labels.icon), el('span', 'sidebar-item-name', item.name));
    if (service === 'queues') anchor.append(el('span', 'count', String(item.approximateMessageCount)));
    if (item.name === active) anchor.setAttribute('aria-current', 'page');
    row.append(anchor);

    // Hiding the button is only a courtesy: the server refuses the delete on a read-only connection anyway.
    if ((service === 'queues' || service === 'tables') && connection && !connection.readOnly) {
      const deleteButton = el('button', 'icon-button danger sidebar-item-delete');
      deleteButton.type = 'button';
      deleteButton.title = service === 'queues' ? 'Delete queue' : 'Delete table';
      deleteButton.setAttribute('aria-label', `Delete ${item.name}`);
      deleteButton.append(icon('trash'));
      deleteButton.addEventListener('click', (event) => {
        event.preventDefault();
        event.stopPropagation();
        if (service === 'queues') deleteQueue(item.name);
        else deleteTable(item.name);
      });
      row.append(deleteButton);
    }

    return row;
  });

  if (items.length > 0 && matching.length === 0) rows.push(el('li', 'empty', labels.empty));

  els.containerFilterField.hidden = items.length === 0;
  els.containerCount.textContent = items.length > 0 ? String(items.length) : '';
  els.containers.replaceChildren(...rows);
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

function renderQueueLocation(queue) {
  const crumbs = queue ? [link('#queues', 'Queues')] : [];

  els.breadcrumb.replaceChildren(...crumbs);
  els.title.textContent = queue || 'Queues';
  els.statusPath.textContent = queue ?? '';
}

function renderTableLocation(table) {
  const crumbs = table ? [link('#tables', 'Tables')] : [];

  els.breadcrumb.replaceChildren(...crumbs);
  els.title.textContent = table || 'Tables';
  els.statusPath.textContent = table ?? '';
}

function hideListing() {
  listed = null;
  found = null;
  els.listing.hidden = true;
  els.toolbar.hidden = true;
  els.itemCount.textContent = '';
}

function hideQueueListing() {
  queueMessages = null;
  els.queueListing.hidden = true;
  els.itemCount.textContent = '';
}

function clearEntities() {
  tableRows = [];
  tableColumns = [];
  tableContinuationToken = null;
  tableLoadedMore = false;
  els.tableListing.hidden = true;
  els.loadMoreBar.hidden = true;
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
  if (entry.isFolder) {
    // Hiding the button is only a courtesy: the server refuses the delete on a read-only connection anyway.
    if (connection && !connection.readOnly) {
      const deleteButton = el('button', 'icon-button danger');
      deleteButton.type = 'button';
      deleteButton.title = 'Delete folder';
      deleteButton.setAttribute('aria-label', `Delete folder ${entry.name}`);
      deleteButton.append(icon('trash'));
      deleteButton.addEventListener('click', () => deleteFolder(container, entry));
      group.append(deleteButton);
    }
  } else {
    if (previewKindOf(entry)) {
      const preview = el('button', 'icon-button outlined');
      preview.type = 'button';
      preview.title = 'Preview';
      preview.setAttribute('aria-label', `Preview ${entry.name}`);
      preview.append(icon('eye'));
      preview.addEventListener('click', () => openPreview(container, entry));
      group.append(preview);
    }

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
  // Named "route" and not "location" so it cannot be confused with (or shadow) window.location, which the rest of
  // this file uses to navigate.
  const route = parseHash();
  service = route.service;

  renderServiceSwitch();
  renderSidebarHeading();

  // The other services' panels are left exactly as they were, so switching back to one does not flash empty first;
  // only the ones not shown are forced hidden here, and the active one manages its own listing and toolbar as it
  // renders.
  if (service !== 'blobs') {
    els.listing.hidden = true;
    els.toolbar.hidden = true;
    els.blobActions.hidden = true;
  }
  if (service !== 'queues') {
    els.queueListing.hidden = true;
    els.queueToolbar.hidden = true;
    els.queueNote.hidden = true;
  }
  if (service !== 'tables') {
    els.tableListing.hidden = true;
    els.tableToolbar.hidden = true;
    els.loadMoreBar.hidden = true;
  }

  if (service === 'queues') await renderQueuesView(route.name);
  else if (service === 'tables') await renderTablesView(route.name);
  else await renderBlobsView(route.container, route.prefix);
}

async function renderBlobsView(container, prefix) {
  const token = ++renderToken;

  // A search in flight is about a listing that is being replaced.
  searchToken++;
  clearTimeout(searchTimer);

  renderSidebarList(container);
  renderLocation(container, prefix);
  // Hiding the button is only a courtesy: the server refuses the upload on a read-only connection anyway.
  els.blobActions.hidden = !(container && connection && !connection.readOnly);

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

const queueFilterTerm = () => els.queueFilter.value.trim();

// Peeking reads a handful of messages at once, so this filters the ones already on screen instead of asking the
// server again.
function matchingMessages() {
  const needle = queueFilterTerm().toLowerCase();
  const messages = queueMessages ?? [];

  return needle
    ? messages.filter((m) => m.text.toLowerCase().includes(needle) || m.messageId.toLowerCase().includes(needle))
    : messages;
}

async function renderQueuesView(queue) {
  const token = ++renderToken;
  if (queue !== activeQueue) {
    els.queueFilter.value = '';
    expandedMessages = new Set();
  }
  activeQueue = queue || null;

  renderSidebarList(activeQueue);
  renderQueueLocation(activeQueue);

  if (!activeQueue) {
    els.queueToolbar.hidden = true;
    els.queueNote.hidden = true;
    hideQueueListing();
    showMessage(queues.length === 0 ? 'No queues found.' : 'Select a queue to peek its messages.');
    return;
  }

  try {
    const messages = await getJson(queueMessagesUrl(activeQueue));
    if (token !== renderToken) return;
    queueMessages = messages;
    renderQueueMessages();
  } catch (error) {
    if (token !== renderToken) return;
    els.queueToolbar.hidden = true;
    els.queueNote.hidden = true;
    hideQueueListing();
    showMessage(`Could not peek "${activeQueue}": ${error.message}`, true);
  }
}

function renderQueueMessages() {
  const all = queueMessages ?? [];
  const messages = matchingMessages();
  const term = queueFilterTerm();

  els.messagesBody.replaceChildren(
    ...messages.flatMap((message, index) => {
      const expanded = expandedMessages.has(message.messageId);
      return [renderMessageRow(message, index, expanded), renderMessageDetailRow(message, expanded)];
    }),
  );
  els.queueToolbar.hidden = all.length === 0;
  els.queueNote.hidden = all.length === 0;
  els.queueListing.hidden = messages.length === 0;
  els.queueFilterCount.textContent = term ? `${messages.length} found` : '';

  // Hiding the buttons is only a courtesy: the server refuses either delete on a read-only connection anyway.
  const canWrite = connection && !connection.readOnly;
  els.deletePeekedMessages.hidden = !canWrite;
  els.clearQueue.hidden = !canWrite;
  els.itemCount.textContent = messages.length > 0 ? plural(messages.length, 'message') : '';

  if (all.length === 0) showMessage('This queue has no messages to peek right now.');
  else if (messages.length === 0) showMessage(`No peeked messages match "${term}".`);
  else showMessage('');
}

function toggleMessageDetail(messageId) {
  if (expandedMessages.has(messageId)) expandedMessages.delete(messageId);
  else expandedMessages.add(messageId);
  renderQueueMessages();
}

function renderMessageRow(message, index, expanded) {
  const row = el('tr');

  const num = el('td', 'col-index num', String(index + 1));

  const id = el('td', 'col-message-id', message.messageId);
  id.title = message.messageId;

  const text = el('td', 'col-message-text', message.text);
  text.title = message.text;

  const inserted = el('td', 'col-inserted', formatDate(message.insertedOn));
  if (message.insertedOn) inserted.title = new Date(message.insertedOn).toLocaleString();

  const expires = el('td', 'col-expires', formatDate(message.expiresOn));
  if (message.expiresOn) expires.title = new Date(message.expiresOn).toLocaleString();

  const dequeue = el('td', 'num', String(message.dequeueCount));

  const expand = el('td', 'col-expand');
  const toggle = el('button', 'icon-button expand-toggle');
  toggle.type = 'button';
  toggle.setAttribute('aria-expanded', String(expanded));
  toggle.setAttribute('aria-label', expanded ? 'Hide details' : 'Show details');
  toggle.append(icon('chevron-down'));
  toggle.addEventListener('click', () => toggleMessageDetail(message.messageId));
  expand.append(toggle);

  row.append(num, id, text, inserted, expires, dequeue, expand);
  return row;
}

// The decoded body, the raw text behind it, and the exact (not "Today, …") dates: everything a collapsed row has no
// room for. Copying is the one thing here that changes nothing in the account, so it needs no read-only guard.
function renderMessageDetailRow(message, expanded) {
  const row = el('tr', 'message-detail');
  row.hidden = !expanded;

  const cell = el('td');
  cell.colSpan = 7;

  const body = el('div', 'message-detail-body');

  const left = el('div', 'message-detail-left');
  const heading = el('div', 'message-detail-heading');
  heading.append(el('span', 'message-detail-label', 'Body'));
  if (message.textWasBase64Decoded) heading.append(el('span', 'tag info', 'Base64 decoded'));
  left.append(heading, el('pre', 'message-detail-box', message.text));

  const right = el('div', 'message-detail-right');
  const actions = el('div', 'message-detail-actions');
  const copyBody = el('button', 'primary-button', 'Copy body');
  copyBody.type = 'button';
  copyBody.addEventListener('click', () => copyToClipboard(message.text, 'Body'));
  const copyMessageId = el('button', 'outlined-button', 'Copy Message Id');
  copyMessageId.type = 'button';
  copyMessageId.addEventListener('click', () => copyToClipboard(message.messageId, 'Message Id'));
  const copyRaw = el('button', 'outlined-button', 'Copy raw');
  copyRaw.type = 'button';
  copyRaw.addEventListener('click', () => copyToClipboard(message.rawText, 'Raw text'));
  actions.append(copyBody, copyMessageId, copyRaw);

  const facts = el('dl', 'message-detail-facts');
  const addFact = (term, value) => facts.append(el('dt', undefined, term), el('dd', undefined, value));
  addFact('Inserted', message.insertedOn ? new Date(message.insertedOn).toLocaleString() : '—');
  addFact('Expires', message.expiresOn ? new Date(message.expiresOn).toLocaleString() : '—');
  addFact('Dequeue count', String(message.dequeueCount));

  right.append(actions, facts);
  body.append(left, right);
  cell.append(body);
  row.append(cell);
  return row;
}

async function copyToClipboard(text, label) {
  try {
    await navigator.clipboard.writeText(text);
    setStatus(`${label} copied.`);
  } catch (error) {
    showMessage(`Could not copy: ${error.message}`, true);
  }
}

const tableFilterTerm = () => els.tableFilter.value.trim();

async function renderTablesView(table) {
  const token = ++renderToken;
  if (table !== activeTable) els.tableFilter.value = '';
  activeTable = table || null;

  renderSidebarList(activeTable);
  renderTableLocation(activeTable);

  if (!activeTable) {
    els.tableToolbar.hidden = true;
    clearEntities();
    showMessage(tables.length === 0 ? 'No tables found.' : 'Select a table to query its entities.');
    return;
  }

  els.tableToolbar.hidden = false;
  await queryEntities(token, tableFilterTerm());
}

// Runs a fresh query (its first page) for the active table with the given filter, replacing whatever was on screen.
// The toolbar (so the filter can be fixed and retried) is left alone; only the listing reacts to a failure.
async function queryEntities(token, filter) {
  clearEntities();
  els.tableFilterCount.textContent = filter ? 'Querying…' : '';

  try {
    const page = await getJson(tableEntitiesUrl(activeTable, filter, null));
    if (token !== renderToken) return;
    tableColumns = page.columns;
    tableRows = page.entities;
    tableContinuationToken = page.continuationToken;
    renderEntitiesTable();
  } catch (error) {
    if (token !== renderToken) return;
    els.tableFilterCount.textContent = '';
    showMessage(`Could not query "${activeTable}": ${error.message}`, true);
  }
}

// Reads the next page with the same table and filter and appends it, instead of replacing the page queryEntities
// fetched. The button is disabled for the duration so a second click cannot fetch the same page twice.
async function loadMoreEntities() {
  if (!activeTable || !tableContinuationToken) return;

  const token = renderToken;
  els.loadMore.disabled = true;
  try {
    const page = await getJson(tableEntitiesUrl(activeTable, tableFilterTerm(), tableContinuationToken));
    if (token !== renderToken) return;
    tableColumns = mergeColumns(tableColumns, page.columns);
    tableRows = [...tableRows, ...page.entities];
    tableContinuationToken = page.continuationToken;
    tableLoadedMore = true;
    renderEntitiesTable();
  } catch (error) {
    if (token !== renderToken) return;
    showMessage(`Could not load more entities: ${error.message}`, true);
    els.loadMore.disabled = false;
  }
}

// PartitionKey and RowKey stay first and Timestamp last, the same order the server puts them in; only the custom
// columns in between are merged and re-sorted, in case a later page has properties an earlier one did not.
function mergeColumns(existing, incoming) {
  const leading = ['PartitionKey', 'RowKey'];
  const middle = new Set([...existing, ...incoming].filter((column) => !leading.includes(column) && column !== 'Timestamp'));

  return [...leading, ...[...middle].sort(), 'Timestamp'];
}

// Matches TableExplorerService.PageSize: only used to label the "Load next" button, the same way the mockup does.
const TABLE_PAGE_SIZE = 100;

function renderEntitiesTable() {
  // Hiding the column is only a courtesy: the server refuses the delete on a read-only connection anyway.
  const canWrite = connection && !connection.readOnly;

  els.entitiesHeadRow.replaceChildren(
    ...tableColumns.map(renderEntityHeader),
    ...(canWrite ? [actionsHeader()] : []),
  );
  els.entitiesBody.replaceChildren(...tableRows.map((row) => renderEntityRow(row, canWrite)));

  els.tableListing.hidden = tableRows.length === 0;
  els.loadMoreBar.hidden = tableRows.length === 0;
  els.loadMore.hidden = !tableContinuationToken;
  els.loadMore.textContent = `Load next ${TABLE_PAGE_SIZE}`;
  els.loadMore.disabled = false;
  els.loadMoreSummary.textContent = tableRows.length > 0
    ? `Showing ${tableRows.length} ${tableRows.length === 1 ? 'entity' : 'entities'}.${tableContinuationToken ? ' There are more.' : ''}`
    : '';

  const term = tableFilterTerm();
  els.tableFilterCount.textContent = term ? `${tableRows.length} found` : '';
  els.itemCount.textContent = tableRows.length > 0 ? `${tableRows.length} ${tableRows.length === 1 ? 'entity' : 'entities'}` : '';

  if (tableRows.length === 0) showMessage(term ? `No entities match "${term}".` : 'This table has no entities.');
  else showMessage('');
}

function renderEntityHeader(column) {
  const th = el('th');
  const header = el('div', 'col-header');
  header.append(el('span', undefined, column), el('span', 'col-type', inferColumnType(column)));
  th.append(header);
  return th;
}

function actionsHeader() {
  const th = el('th', 'col-actions');
  th.append(el('span', 'sr-only', 'Actions'));
  return th;
}

// The server sends only the column names, not their type: this looks at the values loaded so far for one that says
// it (any row's is good enough, since a column that mixes types is not something this explorer tries to represent).
function inferColumnType(column) {
  if (column === 'Timestamp') return 'datetime';

  for (const row of tableRows) {
    if (!(column in row) || row[column] === null) continue;
    if (typeof row[column] === 'boolean') return 'bool';
    if (typeof row[column] === 'number') return Number.isInteger(row[column]) ? 'int64' : 'double';
    return 'string';
  }

  return 'string';
}

function renderEntityRow(row, canWrite) {
  const tr = el('tr');
  tr.append(...tableColumns.map((column) => renderEntityCell(column, row)));

  if (canWrite) {
    const actions = el('td');
    const group = el('div', 'row-actions');
    const deleteButton = el('button', 'icon-button danger');
    deleteButton.type = 'button';
    deleteButton.title = 'Delete entity';
    deleteButton.setAttribute('aria-label', `Delete entity ${row.PartitionKey}/${row.RowKey}`);
    deleteButton.append(icon('trash'));
    deleteButton.addEventListener('click', () => deleteEntity(activeTable, row.PartitionKey, row.RowKey));
    group.append(deleteButton);
    actions.append(group);
    tr.append(actions);
  }

  return tr;
}

function renderEntityCell(column, row) {
  if (!(column in row)) return el('td', 'none', '—');

  const value = row[column];
  if (typeof value === 'boolean') {
    const td = el('td');
    td.append(el('span', `chip ${value}`, String(value)));
    return td;
  }

  const isKey = column === 'PartitionKey' || column === 'RowKey';
  const className = [isKey && 'entity-key', typeof value === 'number' && 'num'].filter(Boolean).join(' ') || undefined;
  const td = el('td', className, formatEntityValue(column, value));
  if (column === 'Timestamp' && value) td.title = new Date(value).toLocaleString();

  return td;
}

// Every property comes back from the server as whatever JSON has (string, number, boolean or null): there is no way
// to tell a date apart from a string that merely looks like one, so only Timestamp, always a date, is formatted as
// one. A boolean is a chip instead (see renderEntityCell) and never reaches this.
function formatEntityValue(column, value) {
  if (value == null) return '';
  if (column === 'Timestamp') return formatDate(value);
  return String(value);
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
    containers = await getJson(`${API}/blobs/containers`);
  } catch (error) {
    containers = [];
    showMessage(`Could not load containers: ${error.message}`, true);
  }
}

// Only called once the connection is known, since an account without a queue endpoint has nothing to list here (the
// server would otherwise answer 400 for a service the account does not have).
async function loadQueues() {
  if (!connection?.hasQueues) {
    queues = [];
    return;
  }

  try {
    queues = await getJson(queuesUrl);
  } catch (error) {
    queues = [];
    showMessage(`Could not load queues: ${error.message}`, true);
  }
}

// Only called once the connection is known, for the same reason as loadQueues above.
async function loadTables() {
  if (!connection?.hasTables) {
    tables = [];
    return;
  }

  try {
    tables = await getJson(tablesUrl);
  } catch (error) {
    tables = [];
    showMessage(`Could not load tables: ${error.message}`, true);
  }
}

// --- Actions -------------------------------------------------------------------

// Asks before a destructive action, with details specific to what is being acted on. On an account that is not on
// this machine, confirmText must be typed as well.
function confirmAction({ title, localWarning, remoteWarning, details, submitLabel, confirmText }) {
  const remote = connection != null && !connection.isLocal;

  els.confirmTitle.textContent = title;
  els.confirmText.textContent = remote ? remoteWarning : localWarning;

  const accountValue = el('dd');
  const account = el('span', undefined, connection?.accountName ?? '');
  accountValue.replaceChildren(...(remote ? [account, el('span', 'tag', 'Remote')] : [account]));

  const detailRow = (term, value) => {
    const row = el('div');
    const valueNode = typeof value === 'string' ? el('dd', undefined, value) : value;
    row.append(el('dt', undefined, term), valueNode);
    return row;
  };

  els.confirmDetails.replaceChildren(
    detailRow('Account', accountValue),
    ...details.map(([term, value]) => detailRow(term, value)),
  );

  els.confirmSubmit.textContent = submitLabel;

  requiredName = remote ? confirmText : '';
  els.confirmNameField.hidden = !remote;
  els.confirmNameHint.textContent = confirmText;
  els.confirmName.value = '';
  els.confirmSubmit.disabled = remote;

  els.confirmDialog.returnValue = '';

  return new Promise((resolve) => {
    els.confirmDialog.addEventListener('close', () => resolve(els.confirmDialog.returnValue === 'confirm'), { once: true });
    els.confirmDialog.showModal();
    (remote ? els.confirmName : els.confirmCancel).focus();
  });
}

// --- Upload -----------------------------------------------------------------
// Every file is a separate POST of its raw bytes (no multipart form): the button and the drop zone both reduce to a
// list of { path, file }, path being where the blob lands relative to the folder on screen.

function canUploadHere() {
  return service === 'blobs' && listed != null && connection != null && !connection.readOnly;
}

// A handful of uploads at once rather than one connection per file (a dropped folder can hold hundreds) or all of
// them at once (which would let a huge folder open that many connections in one go).
const UPLOAD_CONCURRENCY = 4;

async function uploadFiles(container, prefix, items) {
  if (items.length === 0) return;

  const total = items.length;
  let done = 0;
  const failures = [];

  setStatus(`Uploading 1/${plural(total, 'file')}…`);

  const queue = [...items];
  async function worker() {
    while (queue.length > 0) {
      const item = queue.shift();
      const path = prefix + item.path;
      try {
        await api(blobUrl(container, path), {
          method: 'POST',
          headers: { ...REQUEST_HEADERS, 'Content-Type': item.file.type || 'application/octet-stream' },
          body: item.file,
        });
      } catch (error) {
        failures.push(`${item.path} (${error.message})`);
      }
      done++;
      if (done < total) setStatus(`Uploading ${done + 1}/${plural(total, 'file')}…`);
    }
  }

  await Promise.all(Array.from({ length: Math.min(UPLOAD_CONCURRENCY, items.length) }, worker));
  await render();

  if (failures.length === 0) setStatus(`Uploaded ${plural(total, 'file')}.`);
  else showMessage(`Uploaded ${total - failures.length} of ${plural(total, 'file')}. Failed: ${failures.join(', ')}`, true);
}

// Reads whatever was dropped into a flat list of { path, file }, walking into dropped folders when the browser
// exposes the File System Entry API (every current browser does); a plain file list otherwise.
async function filesFromDataTransfer(dataTransfer) {
  // webkitGetAsEntry() is read synchronously here, before any await: the DataTransferItemList it comes from is only
  // valid for the duration of the event, and some browsers clear it once this handler yields.
  const entries = [...(dataTransfer.items ?? [])].map((item) => item.webkitGetAsEntry?.()).filter(Boolean);

  if (entries.length === 0) return [...dataTransfer.files].map((file) => ({ path: file.name, file }));

  const items = [];
  await Promise.all(entries.map((entry) => walkEntry(entry, '', items)));
  return items;
}

function walkEntry(entry, prefix, items) {
  if (entry.isFile) {
    return new Promise((resolve, reject) => {
      entry.file((file) => {
        items.push({ path: `${prefix}${entry.name}`, file });
        resolve();
      }, reject);
    });
  }

  if (!entry.isDirectory) return Promise.resolve();

  return new Promise((resolve, reject) => {
    const reader = entry.createReader();
    // A directory reader hands out entries in batches (Chrome stops at 100), so it has to be called again and again
    // until it comes back empty, not just once.
    const readNextBatch = () => {
      reader.readEntries(async (batch) => {
        if (batch.length === 0) {
          resolve();
          return;
        }
        await Promise.all(batch.map((child) => walkEntry(child, `${prefix}${entry.name}/`, items)));
        readNextBatch();
      }, reject);
    };
    readNextBatch();
  });
}

async function deleteBlob(container, entry) {
  const fileName = entry.path.slice(entry.path.lastIndexOf('/') + 1);
  const confirmed = await confirmAction({
    title: 'Delete this blob permanently?',
    localWarning: 'It will be removed from the account below. This cannot be undone.',
    remoteWarning: 'It will be removed from an account that is not on this machine. This cannot be undone.',
    details: [['Container', container], ['Blob', entry.path], ['Size', formatSize(entry.size)]],
    submitLabel: 'Delete permanently',
    confirmText: fileName,
  });
  if (!confirmed) return;

  try {
    await api(blobUrl(container, entry.path), { method: 'DELETE', headers: REQUEST_HEADERS });
    setStatus(`Deleted ${entry.path}`);
    await render();
  } catch (error) {
    showMessage(`Could not delete "${entry.path}": ${error.message}`, true);
  }
}

async function deleteFolder(container, entry) {
  setStatus('Counting blobs…');
  let count, truncated;
  try {
    ({ count, truncated } = await getJson(folderUrl(container, entry.path)));
  } catch (error) {
    showMessage(`Could not count the blobs in "${entry.path}": ${error.message}`, true);
    return;
  }

  const countLabel = truncated ? `More than ${plural(count, 'blob')}` : plural(count, 'blob');
  const confirmed = await confirmAction({
    title: 'Delete this folder permanently?',
    localWarning: 'It will be removed from the account below, with every blob in it and below it. This cannot be undone.',
    remoteWarning:
      'It will be removed from an account that is not on this machine, with every blob in it and below it. This ' +
      'cannot be undone.',
    details: [['Container', container], ['Folder', entry.path], ['Blobs', countLabel]],
    submitLabel: 'Delete permanently',
    confirmText: entry.name,
  });
  if (!confirmed) return;

  try {
    const response = await api(folderUrl(container, entry.path), { method: 'DELETE', headers: REQUEST_HEADERS });
    const { deleted } = await response.json();
    setStatus(`Deleted ${plural(deleted, 'blob')} from ${entry.path}`);
    await render();
  } catch (error) {
    showMessage(`Could not delete "${entry.path}": ${error.message}`, true);
  }
}

function prettyJson(text) {
  try {
    return JSON.stringify(JSON.parse(text), null, 2);
  } catch {
    // Not valid JSON (or too unusual to parse): show it exactly as stored rather than fail the preview.
    return text;
  }
}

async function openPreview(container, entry) {
  const kind = previewKindOf(entry);

  els.previewTitle.textContent = entry.name;
  els.previewMeta.textContent = [entry.contentType || 'Unknown type', formatSize(entry.size)].join(' · ');
  els.previewDownload.href = blobUrl(container, entry.path);
  els.previewBody.replaceChildren();
  els.previewDialog.showModal();

  if (entry.size != null && entry.size > MAX_PREVIEW_BYTES) {
    els.previewBody.append(el('p', 'preview-message', `This file is too large to preview (${formatSize(entry.size)}). Download it instead.`));
    return;
  }

  if (kind === 'image') {
    const img = el('img', 'preview-image');
    img.src = previewUrl(container, entry.path);
    img.alt = entry.name;
    els.previewBody.append(img);
    return;
  }

  if (kind === 'pdf') {
    const frame = el('iframe', 'preview-frame');
    frame.src = previewUrl(container, entry.path);
    frame.title = entry.name;
    els.previewBody.append(frame);
    return;
  }

  // json or text: fetched and shown as plain text (never innerHTML), so whatever the blob contains is always
  // displayed literally and cannot run as script, no matter what its content type claims to be.
  els.previewBody.append(el('p', 'preview-message', 'Loading…'));
  try {
    const response = await fetch(previewUrl(container, entry.path));
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const text = await response.text();
    const pre = el('pre', 'preview-text');
    pre.textContent = kind === 'json' ? prettyJson(text) : text;
    els.previewBody.replaceChildren(pre);
  } catch (error) {
    els.previewBody.replaceChildren(el('p', 'preview-message error', `Could not load preview: ${error.message}`));
  }
}

async function deleteQueue(name) {
  const confirmed = await confirmAction({
    title: 'Delete this queue permanently?',
    localWarning: 'It will be removed from the account below, with every message in it. This cannot be undone.',
    remoteWarning: 'It will be removed from an account that is not on this machine, with every message in it. This cannot be undone.',
    details: [['Queue', name]],
    submitLabel: 'Delete permanently',
    confirmText: name,
  });
  if (!confirmed) return;

  try {
    await api(queueUrl(name), { method: 'DELETE', headers: REQUEST_HEADERS });
    setStatus(`Deleted queue ${name}`);
    // The list itself, not just the active queue's messages, needs reloading: render() alone would still show the
    // queue just deleted, since it only re-renders the sidebar from what loadQueues() last fetched.
    if (name === activeQueue) location.hash = hashForQueue();
    await refresh();
  } catch (error) {
    showMessage(`Could not delete queue "${name}": ${error.message}`, true);
  }
}

async function clearQueue(name) {
  const count = queues.find((q) => q.name === name)?.approximateMessageCount ?? 0;
  const confirmed = await confirmAction({
    title: 'Clear this queue permanently?',
    localWarning: 'Every message in it will be removed, including ones not shown here. This cannot be undone.',
    remoteWarning:
      'Every message in it will be removed, including ones not shown here, on an account that is not on this ' +
      'machine. This cannot be undone.',
    details: [['Queue', name], ['Messages', plural(count, 'message')]],
    submitLabel: 'Clear queue',
    confirmText: name,
  });
  if (!confirmed) return;

  try {
    await api(queueMessagesUrl(name), { method: 'DELETE', headers: REQUEST_HEADERS });
    setStatus(`Cleared queue ${name}`);
    // The sidebar's message count badge is stale otherwise: it only updates on the next loadQueues(), not on render().
    await refresh();
  } catch (error) {
    showMessage(`Could not clear queue "${name}": ${error.message}`, true);
  }
}

async function deletePeekedMessages(name) {
  const count = queueMessages?.length ?? 0;
  const confirmed = await confirmAction({
    title: 'Delete the peeked messages?',
    localWarning: 'Only what is shown here will be removed; anything further back in the queue is left alone. This cannot be undone.',
    remoteWarning:
      'Only what is shown here will be removed; anything further back in the queue is left alone, on an account ' +
      'that is not on this machine. This cannot be undone.',
    details: [['Queue', name], ['Messages', plural(count, 'message')]],
    submitLabel: 'Delete peeked messages',
    confirmText: name,
  });
  if (!confirmed) return;

  try {
    const response = await api(queuePeekedMessagesUrl(name), { method: 'DELETE', headers: REQUEST_HEADERS });
    const { deleted } = await response.json();
    setStatus(`Deleted ${plural(deleted, 'message')} from ${name}`);
    // The sidebar's message count badge is stale otherwise: it only updates on the next loadQueues(), not on render().
    await refresh();
  } catch (error) {
    showMessage(`Could not delete the peeked messages of "${name}": ${error.message}`, true);
  }
}

async function deleteTable(name) {
  const confirmed = await confirmAction({
    title: 'Delete this table permanently?',
    localWarning: 'It will be removed from the account below, with every entity in it. This cannot be undone.',
    remoteWarning: 'It will be removed from an account that is not on this machine, with every entity in it. This cannot be undone.',
    details: [['Table', name]],
    submitLabel: 'Delete permanently',
    confirmText: name,
  });
  if (!confirmed) return;

  try {
    await api(tableUrl(name), { method: 'DELETE', headers: REQUEST_HEADERS });
    setStatus(`Deleted table ${name}`);
    // The list itself, not just the active table's entities, needs reloading: render() alone would still show the
    // table just deleted, since it only re-renders the sidebar from what loadTables() last fetched.
    if (name === activeTable) location.hash = hashForTable();
    await refresh();
  } catch (error) {
    showMessage(`Could not delete table "${name}": ${error.message}`, true);
  }
}

async function deleteEntity(table, partitionKey, rowKey) {
  const confirmed = await confirmAction({
    title: 'Delete this entity permanently?',
    localWarning: 'It will be removed from the account below. This cannot be undone.',
    remoteWarning: 'It will be removed from an account that is not on this machine. This cannot be undone.',
    details: [['Table', table], ['Partition Key', partitionKey], ['Row Key', rowKey]],
    submitLabel: 'Delete permanently',
    confirmText: rowKey,
  });
  if (!confirmed) return;

  try {
    await api(entityUrl(table, partitionKey, rowKey), { method: 'DELETE', headers: REQUEST_HEADERS });
    setStatus(`Deleted entity ${partitionKey}/${rowKey}`);
    await render();
  } catch (error) {
    showMessage(`Could not delete the entity: ${error.message}`, true);
  }
}

async function refresh() {
  await Promise.all([loadConnection(), loadContainers()]);
  // Both need connection.hasQueues/hasTables, so they run once loadConnection has set them.
  await Promise.all([loadQueues(), loadTables()]);
  await render();
}

// --- Auto-refresh ----------------------------------------------------------------
// Azure Storage (and Azurite) has no push notification this tool can subscribe to, so noticing a write made from
// outside this tab — the CLI, Azure Storage Explorer, another process — means asking again. Polling stays scoped to
// what is actually on screen (the sidebar list for the active service, and the listing/peek/query the active item
// shows), pauses while the tab is hidden, and skips re-rendering when the answer did not change, so a quiet account
// never disturbs scroll position or an expanded row just because a timer fired.

let pollTimer = null;

function startPolling() {
  if (pollTimer) return;
  pollTimer = setInterval(poll, POLL_INTERVAL_MS);
}

function stopPolling() {
  clearInterval(pollTimer);
  pollTimer = null;
}

async function poll() {
  if (document.hidden) return;

  try {
    if (service === 'queues') await pollQueues();
    else if (service === 'tables') await pollTables();
    else await pollBlobs();
  } catch {
    // Silent: the next tick, or the refresh button, tries again. Surfacing a timer's own failure would be noise for
    // something the user never asked for.
  }
}

const sameJson = (a, b) => JSON.stringify(a) === JSON.stringify(b);

async function pollBlobs() {
  const token = renderToken;

  const freshContainers = await getJson(`${API}/blobs/containers`);
  if (token !== renderToken) return;
  if (!sameJson(freshContainers, containers)) {
    containers = freshContainers;
    renderSidebarList(listed?.container ?? null);
  }

  if (!listed) return;
  const { container, prefix } = listed;
  const listing = await getJson(`${containerUrl(container)}/entries?prefix=${encodeURIComponent(prefix)}`);
  if (token !== renderToken || sameJson(listing, listed.listing)) return;

  listed = { container, prefix, listing };
  // Same as a manual refresh: a search in flight is about a listing that just changed under it.
  if (currentTerm()) await search();
  else { found = null; renderEntries(); }
}

async function pollQueues() {
  const token = renderToken;

  const freshQueues = connection?.hasQueues ? await getJson(queuesUrl) : [];
  if (token !== renderToken) return;
  if (!sameJson(freshQueues, queues)) {
    queues = freshQueues;
    renderSidebarList(activeQueue);
  }

  if (!activeQueue) return;
  const messages = await getJson(queueMessagesUrl(activeQueue));
  if (token !== renderToken || sameJson(messages, queueMessages)) return;

  queueMessages = messages;
  renderQueueMessages();
}

async function pollTables() {
  const token = renderToken;

  const freshTables = connection?.hasTables ? await getJson(tablesUrl) : [];
  if (token !== renderToken) return;
  if (!sameJson(freshTables, tables)) {
    tables = freshTables;
    renderSidebarList(activeTable);
  }

  // A page appended by "Load more" carries state (the continuation token, the rows loaded so far) that a first-page
  // query would throw away, so polling leaves it alone here; the refresh button still re-queries from the start.
  if (!activeTable || tableLoadedMore) return;

  const page = await getJson(tableEntitiesUrl(activeTable, tableFilterTerm(), null));
  if (token !== renderToken || tableLoadedMore) return;
  if (sameJson(page.columns, tableColumns) && sameJson(page.entities, tableRows)) return;

  tableColumns = page.columns;
  tableRows = page.entities;
  tableContinuationToken = page.continuationToken;
  renderEntitiesTable();
}

els.previewClose.addEventListener('click', () => els.previewDialog.close());
// A native <dialog> reports the dialog element itself as the click target for a click on its backdrop (there is no
// separate element to listen on), and also for one that lands in the dialog's own padding around the form. The
// bounding rect tells those two apart: only outside it is the actual backdrop.
els.previewDialog.addEventListener('click', (event) => {
  if (event.target !== els.previewDialog) return;
  const rect = els.previewDialog.getBoundingClientRect();
  const inside = event.clientX >= rect.left && event.clientX <= rect.right && event.clientY >= rect.top && event.clientY <= rect.bottom;
  if (!inside) els.previewDialog.close();
});
// Drop the body's content (an <img>/<iframe> can be a loaded image or PDF) rather than keep it around, however
// the dialog closes (the close button, Esc, or a click on the backdrop).
els.previewDialog.addEventListener('close', () => els.previewBody.replaceChildren());

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

els.containerFilter.addEventListener('input', () => {
  const route = parseHash();
  renderSidebarList(service === 'blobs' ? route.container : route.name);
});
els.containerFilter.addEventListener('keydown', (event) => {
  // Enter opens the first item that matches.
  const [first] = matchingSidebarItems();
  if (event.key !== 'Enter' || !sidebarTerm() || !first) return;

  location.hash = hashForItem(first.name);
});

// "/" jumps to the sidebar search, as it does in most tools with a list on the side.
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

for (const button of els.serviceButtons) {
  button.addEventListener('click', () => {
    if (button.getAttribute('aria-disabled') === 'true') return;

    const target = button.dataset.service;
    if (target === service) return;

    location.hash = target === 'queues' ? hashForQueue() : target === 'tables' ? hashForTable() : '#/';
  });
}

els.queueFilter.addEventListener('input', renderQueueMessages);
els.deletePeekedMessages.addEventListener('click', () => { if (activeQueue) deletePeekedMessages(activeQueue); });
els.clearQueue.addEventListener('click', () => { if (activeQueue) clearQueue(activeQueue); });

els.tableToolbar.addEventListener('submit', (event) => {
  event.preventDefault();
  if (activeTable) queryEntities(++renderToken, tableFilterTerm());
});
els.tableFilterClear.addEventListener('click', () => {
  if (!activeTable || !tableFilterTerm()) return;
  els.tableFilter.value = '';
  queryEntities(++renderToken, '');
});
els.loadMore.addEventListener('click', loadMoreEntities);

// --- Create container / folder --------------------------------------------------
// One generic "type a name" dialog, reused for both: it calls onSubmit(name) and closes on success, showing
// onSubmit's error inline instead of closing, the same shape as the connection dialog below.

let promptOnSubmit = null;

function showPromptError(text) {
  els.promptError.hidden = !text;
  els.promptError.textContent = text ?? '';
}

function setPromptBusy(busy, idleLabel, busyLabel) {
  els.promptSubmit.disabled = busy;
  els.promptCancel.disabled = busy;
  els.promptSubmit.textContent = busy ? busyLabel : idleLabel;
}

function openPrompt({ title, label, submitLabel, busyLabel, onSubmit }) {
  els.promptTitle.textContent = title;
  els.promptLabel.textContent = label;
  els.promptInput.value = '';
  showPromptError('');
  setPromptBusy(false, submitLabel, busyLabel);

  promptOnSubmit = async () => {
    const name = els.promptInput.value.trim();
    if (!name) return;

    setPromptBusy(true, submitLabel, busyLabel);
    showPromptError('');
    try {
      await onSubmit(name);
      els.promptDialog.close();
    } catch (error) {
      showPromptError(error.message);
    } finally {
      setPromptBusy(false, submitLabel, busyLabel);
    }
  };

  els.promptDialog.showModal();
  els.promptInput.focus();
}

els.promptCancel.addEventListener('click', () => els.promptDialog.close());
els.promptForm.addEventListener('submit', (event) => {
  event.preventDefault();
  promptOnSubmit?.();
});

els.newContainerButton.addEventListener('click', () => {
  openPrompt({
    title: 'New container',
    label: 'Container name',
    submitLabel: 'Create',
    busyLabel: 'Creating…',
    onSubmit: async (name) => {
      await api(containerUrl(name), { method: 'POST', headers: REQUEST_HEADERS });
      setStatus(`Created container ${name}`);
      await refresh();
    },
  });
});

els.newFolderButton.addEventListener('click', () => {
  if (!listed) return;
  const { container, prefix } = listed;

  openPrompt({
    title: 'New folder',
    label: 'Folder name',
    submitLabel: 'Create',
    busyLabel: 'Creating…',
    onSubmit: async (name) => {
      const path = `${prefix}${name.replace(/^\/+|\/+$/g, '')}/`;
      await api(`${containerUrl(container)}/folder?path=${encodeURIComponent(path)}`, { method: 'POST', headers: REQUEST_HEADERS });
      setStatus(`Created folder ${name}`);
      await render();
    },
  });
});

// --- Upload button and drag & drop ----------------------------------------------

els.uploadButton.addEventListener('click', () => els.uploadInput.click());
els.uploadInput.addEventListener('change', () => {
  const files = [...els.uploadInput.files];
  els.uploadInput.value = ''; // Otherwise choosing the same file again later would not fire "change".
  if (files.length === 0 || !canUploadHere()) return;

  const { container, prefix } = listed;
  uploadFiles(container, prefix, files.map((file) => ({ path: file.name, file })));
});

const hasFilesToDrop = (dataTransfer) => dataTransfer?.types?.includes('Files') ?? false;

// Not preventDefault-ing dragenter/dragover when uploading is not possible here (no container selected, read-only,
// another service on screen) leaves the browser's own "drop not allowed" cursor in charge, with no extra messaging.
els.panel.addEventListener('dragenter', (event) => {
  if (!canUploadHere() || !hasFilesToDrop(event.dataTransfer)) return;
  event.preventDefault();
  dragDepth++;
  els.dropOverlay.hidden = false;
});
els.panel.addEventListener('dragover', (event) => {
  if (!canUploadHere() || !hasFilesToDrop(event.dataTransfer)) return;
  event.preventDefault();
  event.dataTransfer.dropEffect = 'copy';
});
els.panel.addEventListener('dragleave', () => {
  if (dragDepth === 0) return;
  dragDepth--;
  if (dragDepth === 0) els.dropOverlay.hidden = true;
});
els.panel.addEventListener('drop', async (event) => {
  if (!canUploadHere() || !hasFilesToDrop(event.dataTransfer)) return;
  event.preventDefault();
  dragDepth = 0;
  els.dropOverlay.hidden = true;

  const { container, prefix } = listed;
  const items = await filesFromDataTransfer(event.dataTransfer);
  await uploadFiles(container, prefix, items);
});

els.refresh.addEventListener('click', refresh);
window.addEventListener('hashchange', render);

// Polling pauses the moment the tab is hidden and catches up with an immediate poll
document.addEventListener('visibilitychange', () => {
  if (document.hidden) stopPolling();
  else { poll(); startPolling(); }
});
if (!document.hidden) startPolling();

renderScope();
renderTheme();
refresh();
