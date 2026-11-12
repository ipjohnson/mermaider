// Renders every .mmd in a corpus folder with the mermaid.js pinned in package-lock.json, in one headless page, and writes
// <name>.mjs.facts.txt per case: whether mermaid.js accepts the source, and when it does, the labels, groups, edges and
// end markers of flowchart, state, class and sequence diagrams. MjsFactsTests diffs these with Mermaider's own facts.
//
//   node scripts/mjs-facts.mjs <corpus-dir> <facts-dir> [svg-dir]
//
// Run it through `mise run mjs-facts` (or scripts/mjs-facts.sh), which exports the corpus first. Facts files whose case
// is no longer in the corpus are deleted. With svg-dir, each accepted case's mermaid.js SVG is written there too.
import fs from 'node:fs'
import path from 'node:path'
import crypto from 'node:crypto'
import { fileURLToPath } from 'node:url'
import puppeteer from 'puppeteer'

const [, , corpusDir, factsDir, svgDir] = process.argv
if (!corpusDir || !factsDir) {
	console.error('usage: node scripts/mjs-facts.mjs <corpus-dir> <facts-dir> [svg-dir]')
	process.exit(2)
}

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const mermaidDir = path.join(root, 'node_modules', 'mermaid')
const version = JSON.parse(fs.readFileSync(path.join(mermaidDir, 'package.json'), 'utf8')).version
const pinned = JSON.parse(fs.readFileSync(path.join(root, 'package-lock.json'), 'utf8')).packages['node_modules/mermaid'].version
if (version !== pinned) {
	console.error(`node_modules has mermaid ${version}, package-lock.json pins ${pinned}; run npm ci`)
	process.exit(1)
}

// Hash of the source with CRLF folded to LF; MjsFactsTests computes the same hash to spot a stale facts file.
const sourceHash = text => crypto.createHash('sha256').update(text.replace(/\r\n/g, '\n'), 'utf8').digest('hex').slice(0, 16)

const browser = await puppeteer.launch({ headless: 'shell' })
const page = await browser.newPage()
await page.setContent('<!doctype html><html><body></body></html>')
await page.addScriptTag({ path: path.join(mermaidDir, 'dist', 'mermaid.min.js') })
await page.evaluate(() => mermaid.initialize({ startOnLoad: false, securityLevel: 'strict' }))

fs.mkdirSync(factsDir, { recursive: true })
if (svgDir) fs.mkdirSync(svgDir, { recursive: true })

const names = fs.readdirSync(corpusDir).filter(f => f.endsWith('.mmd')).sort().map(f => f.slice(0, -4))
let accepted = 0
for (const name of names) {
	const source = fs.readFileSync(path.join(corpusDir, name + '.mmd'), 'utf8')
	const result = await page.evaluate(extractFacts, source, name)
	const header = [
		'# mermaid.js facts. Refresh with: mise run mjs-facts',
		`# mermaid ${version}`,
		`# source ${sourceHash(source)}`,
	]
	const body = result.error === undefined
		? ['accept\tyes', ...result.facts.sort()]
		: ['accept\tno', `# error ${result.error.split('\n')[0].trim()}`]
	fs.writeFileSync(path.join(factsDir, name + '.mjs.facts.txt'), [...header, ...body].join('\n') + '\n')
	if (svgDir && result.svg) fs.writeFileSync(path.join(svgDir, name + '.svg'), result.svg)
	if (result.error === undefined) accepted++
	console.log(`${result.error === undefined ? 'ok ' : 'err'}  ${name}${result.error === undefined ? '' : ': ' + result.error.split('\n')[0]}`)
}

const keep = new Set(names.map(n => n + '.mjs.facts.txt'))
for (const f of fs.readdirSync(factsDir).filter(f => f.endsWith('.mjs.facts.txt') && !keep.has(f))) {
	fs.unlinkSync(path.join(factsDir, f))
	console.log(`del  ${f}`)
}

await browser.close()
console.log(`mermaid ${version}: ${names.length} cases, ${accepted} accepted`)

// Runs inside the page. Structure comes from the diagram's db; label text comes from the rendered DOM, because the db
// keeps entities as placeholders and markdown unrendered.
async function extractFacts(source, name) {
	const norm = s => String(s ?? '').replace(/\s+/g, ' ').trim()
	// mermaid's own entity placeholders (see encodeEntities in mermaid's utils), then HTML entities and <br>.
	const decode = s => {
		const html = String(s ?? '').replace(/ﬂ°°/g, '&#').replace(/ﬂ°/g, '&').replace(/¶ß/g, ';').replace(/<br\s*\/?>/gi, ' ')
		const el = document.createElement('div')
		el.innerHTML = html
		return norm(el.textContent)
	}
	const tab = (...parts) => parts.map(p => norm(p).replace(/\t/g, ' ')).join('\t').replace(/\t+$/, '')

	let diagram
	try {
		diagram = await mermaid.mermaidAPI.getDiagramFromText(source)
	} catch (e) {
		return { error: String(e?.message ?? e) }
	}
	const svgId = 'mjs' + name.replace(/[^A-Za-z0-9]/g, '')
	let svg
	try {
		svg = (await mermaid.render(svgId, source)).svg
	} catch (e) {
		return { error: String(e?.message ?? e) }
	}
	// render() parses the source again into a fresh db; re-read it so the db matches the DOM.
	diagram = await mermaid.mermaidAPI.getDiagramFromText(source)
	const host = document.createElement('div')
	host.innerHTML = svg
	document.body.replaceChildren(host)
	const dom = host.querySelector('svg')
	// textContent runs the lines of a <br> together; read each <br> as a space.
	const text = el => {
		if (!el) return ''
		const copy = el.cloneNode(true)
		for (const br of copy.querySelectorAll('br')) br.replaceWith(' ')
		return norm(copy.textContent)
	}
	// A domId ends in a counter that moves between parses, so fall back to the same id with any counter.
	const byDomId = domId => {
		const exact = dom.querySelector(`[id="${svgId}-${domId}"]`)
		if (exact || !domId) return exact
		const stem = `${svgId}-${domId.replace(/-\d+$/, '')}-`
		return [...dom.querySelectorAll('[id]')].find(el => el.id.startsWith(stem) && /^\d+$/.test(el.id.slice(stem.length))) ?? null
	}
	const edgeLabel = id => text(dom.querySelector(`.edgeLabel .label[data-id="${id}"]`))
	const clusterLabel = id => text(dom.querySelector(`.clusters [data-id="${id}"] .cluster-label`) ?? dom.querySelector(`.clusters [id="${svgId}-${id}"] .cluster-label`))

	const db = diagram.db
	const facts = []
	const type = diagram.type
	if (type === 'flowchart-v2' || type === 'flowchart') flowchart()
	else if (type === 'stateDiagram') state()
	else if (type === 'classDiagram') classDiagram()
	else if (type === 'sequence') sequence()
	return { facts, svg }

	function end(t) {
		switch (t) {
			case 'arrow_point': return 'arrow'
			case 'arrow_circle': return 'circle'
			case 'arrow_cross': return 'cross'
			case 'arrow_barb': return 'arrow'
			case 'extension': return 'triangle'
			case 'composition': return 'diamond-filled'
			case 'aggregation': return 'diamond'
			case 'dependency': return 'arrow'
			case 'lollipop': return 'lollipop'
			case undefined: case null: case 'none': case 'arrow_open': return 'none'
			default: return t
		}
	}

	function members(data) {
		const children = new Map()
		for (const n of data.nodes) {
			if (!n.parentId) continue
			if (!children.has(n.parentId)) children.set(n.parentId, [])
			children.get(n.parentId).push(n.id)
		}
		return id => (children.get(id) ?? []).slice().sort().join(',')
	}

	function flowchart() {
		const data = db.getData()
		const childrenOf = members(data)
		for (const n of data.nodes) {
			if (n.isGroup) facts.push(tab('group', n.id, clusterLabel(n.id), childrenOf(n.id)))
			else facts.push(tab('node', n.id, text(byDomId(n.domId)?.querySelector('.label'))))
		}
		const strokes = new Map(db.getEdges().map(e => [e.id, e.stroke]))
		for (const e of data.edges) {
			const stroke = strokes.get(e.id) ?? e.thickness
			const line = stroke === 'invisible' ? 'invisible' : stroke === 'thick' ? 'thick' : stroke === 'dotted' || e.pattern === 'dotted' ? 'dotted' : 'solid'
			facts.push(tab('edge', e.start, e.end, end(e.arrowTypeStart), end(e.arrowTypeEnd), line, edgeLabel(e.id)))
		}
	}

	function state() {
		const data = db.getData()
		const nodes = new Map(data.nodes.map(n => [n.id, n]))
		// A note sits with its state in a wrapper group "<state>----parent"; lift the wrapper's children to its parent.
		const parentOf = id => {
			let p = nodes.get(id)?.parentId ?? null
			while (p && p.endsWith('----parent')) p = nodes.get(p)?.parentId ?? null
			return p
		}
		const isNote = n => n.shape === 'note' || n.id.includes('----note')
		const name = id => {
			const n = nodes.get(id)
			if (n?.shape === 'stateStart') return `[*]start@${parentOf(id) ?? 'root'}`
			if (n?.shape === 'stateEnd') return `[*]end@${parentOf(id) ?? 'root'}`
			return id
		}
		const children = new Map()
		for (const n of data.nodes) {
			if (n.id.endsWith('----parent') || isNote(n)) continue
			const p = parentOf(n.id)
			if (!p) continue
			if (!children.has(p)) children.set(p, new Set())
			children.get(p).add(name(n.id))
		}
		const seen = new Set()
		for (const n of data.nodes) {
			if (n.id.endsWith('----parent')) continue
			if (isNote(n)) {
				const target = n.id.split('----')[0]
				facts.push(tab('note', target, text(byDomId(n.domId)) || decode(n.label)))
				continue
			}
			if (n.isGroup) {
				facts.push(tab('group', n.id, clusterLabel(n.id), [...(children.get(n.id) ?? [])].sort().join(',')))
				continue
			}
			const id = name(n.id)
			if (seen.has(id)) continue
			seen.add(id)
			const kind = n.shape === 'choice' ? 'choice' : n.shape === 'fork' || n.shape === 'join' ? 'fork' : n.shape === 'stateStart' || n.shape === 'stateEnd' ? 'pseudo' : 'state'
			const label = kind === 'state' ? text(byDomId(n.domId)?.querySelector('.label')) : ''
			facts.push(tab('node', id, label, kind))
		}
		for (const e of data.edges) {
			if (isNote(nodes.get(e.start) ?? {}) || isNote(nodes.get(e.end) ?? {})) continue
			facts.push(tab('edge', name(e.start), name(e.end), end(e.arrowTypeStart), end(e.arrowTypeEnd), 'solid', edgeLabel(e.id)))
		}
	}

	function classDiagram() {
		const data = db.getData()
		const nodes = new Map(data.nodes.map(n => [n.id, n]))
		// A lollipop end is a synthetic interface node; name it by its label, as MermaiderFacts does.
		const interfaces = new Map((db.interfaces ?? []).map(i => [i.id, '()' + decode(i.label)]))
		const name = id => interfaces.get(id) ?? id
		for (const n of data.nodes) {
			if (interfaces.has(n.id)) continue
			if (n.isGroup) {
				const kids = data.nodes.filter(c => c.parentId === n.id).map(c => c.id).sort().join(',')
				facts.push(tab('namespace', n.id, kids))
				continue
			}
			if (n.shape === 'note') continue
			const el = byDomId(n.domId)
			facts.push(tab('class', n.id, text(el?.querySelector('.label-group'))))
			for (const a of el?.querySelectorAll('.annotation-group .label') ?? []) facts.push(tab('annotation', n.id, text(a)))
			for (const m of el?.querySelectorAll('.members-group .label') ?? []) facts.push(tab('attribute', n.id, text(m)))
			for (const m of el?.querySelectorAll('.methods-group .label') ?? []) facts.push(tab('method', n.id, text(m)))
		}
		const notes = db.getNotes?.() ?? []
		for (const note of notes instanceof Map ? notes.values() : Object.values(notes)) facts.push(tab('note', note.class ?? '', decode(note.text)))
		for (const e of data.edges) {
			if (nodes.get(e.start)?.shape === 'note' || nodes.get(e.end)?.shape === 'note') continue
			const line = e.pattern === 'dashed' || e.pattern === 'dotted' ? 'dashed' : 'solid'
			const card = v => { const d = decode(v); return d === 'none' ? '' : d }
			facts.push(tab('relation', name(e.start), name(e.end), end(e.arrowTypeStart), end(e.arrowTypeEnd), line, edgeLabel(e.id), card(e.startLabelRight), card(e.endLabelLeft)))
		}
	}

	function sequence() {
		for (const [id, a] of Object.entries(Object.fromEntries(db.getActors()))) facts.push(tab('actor', id, decode(a.description), a.type ?? 'participant'))
		for (const b of db.getBoxes?.() ?? []) facts.push(tab('box', decode(b.name), (b.actorKeys ?? []).slice().sort().join(',')))
		// mermaid's sequence LINETYPE values.
		const message = {
			0: ['solid', 'filled'], 1: ['dashed', 'filled'], 3: ['solid', 'cross'], 4: ['dashed', 'cross'],
			5: ['solid', 'none'], 6: ['dashed', 'none'], 24: ['solid', 'open'], 25: ['dashed', 'open'],
			33: ['solid', 'filled-both'], 34: ['dashed', 'filled-both'],
		}
		const blockStart = { 10: 'loop', 12: 'alt', 15: 'opt', 19: 'par', 22: 'rect', 27: 'critical', 30: 'break', 32: 'par' }
		const divider = { 13: 'else', 20: 'and', 28: 'option' }
		const placement = { 0: 'left', 1: 'right', 2: 'over' }
		for (const m of db.getMessages()) {
			if (m.type in message) facts.push(tab('message', m.from, m.to, ...message[m.type], decode(m.message)))
			else if (m.type === 2) facts.push(tab('note', placement[m.placement] ?? m.placement, [m.from, m.to].filter((v, i, a) => v && a.indexOf(v) === i).join(','), decode(m.message)))
			else if (m.type in blockStart) facts.push(tab('block', blockStart[m.type], m.type === 22 ? '' : decode(m.message)))
			else if (m.type in divider) facts.push(tab('divider', decode(m.message)))
		}
	}
}
