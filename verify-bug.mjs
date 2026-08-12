import { chromium } from 'playwright'

const URL = 'http://localhost:3000'
const EMAIL = 'andrey.zhukov@dataart.com'
const PASSWORD = 'Pwd_2006!'

const browser = await chromium.launch({ headless: true })
const ctx = await browser.newContext()
const page = await ctx.newPage()
page.on('console', (m) => {
  if (m.type() === 'error') console.log('[console error]', m.text())
})

try {
  await page.goto(URL, { waitUntil: 'domcontentloaded' })
  console.log('PAGE:', page.url())

  // Sign in
  await page.waitForSelector('input[type="email"], input[name="email"]', { timeout: 10000 })
  await page.fill('input[type="email"], input[name="email"]', EMAIL)
  await page.fill('input[type="password"], input[name="password"]', PASSWORD)
  await page.getByRole('button', { name: /sign in/i }).click()
  await page.waitForURL((u) => !u.toString().includes('/login'), { timeout: 15000 })
  console.log('After login URL:', page.url())

  // Wait for sidebar
  const sidebar = page.getByTestId('right-sidebar')
  await sidebar.waitFor({ timeout: 10000 })

  // --- Check 1: Contacts section exists with presence dots ---
  const contactsSection = page.getByTestId('sidebar-contacts-section')
  const contactsVisible = await contactsSection.isVisible()
  console.log('Contacts section visible:', contactsVisible)

  // Title text of contacts
  const contactsTitle = await contactsSection.locator('.collapse-title').textContent()
  console.log('Contacts title text:', contactsTitle?.trim())

  // Count contact items
  const contactItems = page.getByTestId('sidebar-contact-item')
  const contactCount = await contactItems.count()
  console.log('Contact items count:', contactCount)

  // Check presence dots within contacts
  if (contactCount > 0) {
    const first = contactItems.first()
    const dotExists = await first.locator('.badge').count()
    console.log('First contact has presence dot:', dotExists > 0)
  }

  // --- Check 2: initial data-compact value is false (no room opened) ---
  const initialCompact = await sidebar.getAttribute('data-compact')
  console.log('Initial data-compact:', initialCompact)

  // Check rooms section is initially open (collapse-open presence via checked)
  const roomsToggle = page.getByTestId('sidebar-rooms-toggle')
  const roomsInitiallyChecked = await roomsToggle.isChecked()
  console.log('Rooms initially open:', roomsInitiallyChecked)
  const dmsInitiallyChecked = await page.getByTestId('sidebar-dms-toggle').isChecked()
  console.log('DMs initially open:', dmsInitiallyChecked)
  const contactsInitiallyChecked = await page.getByTestId('sidebar-contacts-toggle').isChecked()
  console.log('Contacts initially open:', contactsInitiallyChecked)

  // --- Check 3: open a room and verify sections collapse (auto compaction) ---
  // Find any room link within the sidebar
  const roomLinks = sidebar.locator('a[href*="/rooms/"]')
  const roomCount = await roomLinks.count()
  console.log('Room links in sidebar:', roomCount)

  let clickRoomLinks = roomLinks
  let clickRoomCount = roomCount
  if (clickRoomCount === 0) {
    // Try Public Rooms catalog to find/join a room
    await page.goto(URL + '/rooms')
    await page.waitForTimeout(1000)
    // join any visible room
    const joinBtn = page.getByRole('button', { name: /^(join|open|enter)$/i }).first()
    if (await joinBtn.isVisible().catch(() => false)) {
      await joinBtn.click().catch(() => {})
      await page.waitForTimeout(1500)
    }
    // Try to navigate to a room page manually - find any room card link
    const catalogRoomLinks = page.locator('a[href*="/rooms/"]')
    const catalogCount = await catalogRoomLinks.count()
    console.log('Catalog room links:', catalogCount)
    if (catalogCount > 0) {
      clickRoomLinks = catalogRoomLinks
      clickRoomCount = catalogCount
    }
  }

  if (clickRoomCount > 0) {
    await clickRoomLinks.first().click()
    // Wait for url to include /rooms/
    await page.waitForURL(/\/rooms\//, { timeout: 10000 })
    // Allow the effect to run
    await page.waitForTimeout(500)

    const compactAfter = await sidebar.getAttribute('data-compact')
    console.log('data-compact after opening room:', compactAfter)

    const roomsOpenAfter = await roomsToggle.isChecked()
    const dmsOpenAfter = await page.getByTestId('sidebar-dms-toggle').isChecked()
    const contactsOpenAfter = await page.getByTestId('sidebar-contacts-toggle').isChecked()
    console.log('Rooms open after room:', roomsOpenAfter)
    console.log('DMs open after room:', dmsOpenAfter)
    console.log('Contacts open after room:', contactsOpenAfter)

    const allCollapsed = !roomsOpenAfter && !dmsOpenAfter && !contactsOpenAfter
    console.log('All sections auto-collapsed:', allCollapsed)
    console.log('VERIFY_AUTO_COMPACT:', compactAfter === 'true' && allCollapsed ? 'PASS' : 'FAIL')
  } else {
    console.log('No room links available; auto-compaction check skipped.')
    console.log('VERIFY_AUTO_COMPACT:', 'SKIPPED_NO_ROOMS')
  }

  console.log('VERIFY_CONTACTS_SECTION:', contactsVisible && /contacts/i.test(contactsTitle ?? '') ? 'PASS' : 'FAIL')
} catch (e) {
  console.error('ERROR:', e.message)
  await page.screenshot({ path: 'verify-bug-error.png' }).catch(() => {})
  process.exitCode = 2
} finally {
  await browser.close()
}
